using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.GitAnalysis;

public class GitHistoryParser
{
    public CpCommitGraph ParseHistory(string repositoryPath, string fromBranch, string toBranch)
    {
        using var repo = new Repository(repositoryPath);
        var fromBranchRef = repo.Branches[fromBranch]?.Tip;
        var toBranchRef = repo.Branches[toBranch]?.Tip;

        if (fromBranchRef == null || toBranchRef == null)
        {
            throw new ArgumentException($"Branches '{fromBranch}' or '{toBranch}' not found in the repository.");
        }

        var filter = new CommitFilter
        {
            IncludeReachableFrom = fromBranchRef,
            ExcludeReachableFrom = toBranchRef,
            SortBy = CommitSortStrategies.Reverse
        };

        var commits = new Dictionary<string, CpCommit>();
        var childrenMap = new Dictionary<string, List<string>>();

        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            var sha = commit.Sha;
            var parents = commit.Parents.Select(p => p.Sha).ToList();
            
            // Get modified files by comparing with parent(s)
            var modifiedFiles = GetModifiedFiles(repo, commit);

            commits[sha] = new CpCommit(commit, modifiedFiles);

            // Build children map for navigation
            foreach (var parent in parents)
            {
                if (!childrenMap.ContainsKey(parent))
                    childrenMap[parent] = [];
                childrenMap[parent].Add(sha);
            }
        }

        return new CpCommitGraph
        {
            Commits = commits,
            ChildrenMap = childrenMap,
            FromBranch = fromBranch,
            ToBranch = toBranch
        };
    }

    /// <summary>
    /// Get the list of files modified in a specific commit by comparing with its first parent.
    /// This is perfect for cherry-pick analysis - we want to know what THIS commit changed.
    /// </summary>
    private List<string> GetModifiedFiles(Repository repo, Commit commit)
    {
        var modifiedFiles = new List<string>();

        try
        {
            if (!commit.Parents.Any())
            {
                // Root commit - all files are "added"
                return GetAllFilesInTree(commit.Tree);
            }

            // Compare with first parent only - this is what we want for cherry-pick analysis
            // - Regular commits: first parent is the only parent
            // - Merge commits: first parent is the target branch, so diff shows what was merged
            var parent = commit.Parents.First();
            var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);

            foreach (var change in changes)
            {
                // Include the current path
                modifiedFiles.Add(change.Path);

                // For renamed files, we might want both paths for better matching
                if (change.Status == ChangeKind.Renamed && !string.IsNullOrEmpty(change.OldPath))
                {
                    modifiedFiles.Add(change.OldPath);
                }
            }
        }
        catch (LibGit2SharpException)
        {
            // Handle Git errors gracefully - some commits might have issues
        }

        return modifiedFiles.Distinct().ToList();
    }

    /// <summary>
    /// Get all files in a tree (used for root commits)
    /// </summary>
    private List<string> GetAllFilesInTree(Tree tree, string prefix = "")
    {
        var files = new List<string>();

        foreach (var entry in tree)
        {
            // Use forward slashes for Git paths (cross-platform consistency)
            var fullPath = string.IsNullOrEmpty(prefix) ? entry.Name : $"{prefix}/{entry.Name}";

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                // Recursively get files from subdirectories
                var subtree = (Tree)entry.Target;
                files.AddRange(GetAllFilesInTree(subtree, fullPath));
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                // This is a file
                files.Add(fullPath);
            }
        }

        return files;
    }

    /// <summary>
    /// Get all commit SHAs reachable from a specific branch
    /// </summary>
    public HashSet<string> GetCommitsInBranch(string repositoryPath, string branchName)
    {
        using var repo = new Repository(repositoryPath);
        var branch = repo.Branches[branchName];

        if (branch == null)
        {
            throw new ArgumentException($"Branch '{branchName}' not found in the repository.");
        }

        var filter = new CommitFilter
        {
            IncludeReachableFrom = branch,
            SortBy = CommitSortStrategies.Time
        };

        return repo.Commits.QueryBy(filter).Select(c => c.Sha).ToHashSet();
    }

    /// <summary>
    /// Get detailed information about what changed in each file for a commit
    /// </summary>
    public Dictionary<string, FileChangeInfo> GetDetailedFileChanges(string repositoryPath, string commitSha)
    {
        using var repo = new Repository(repositoryPath);
        var commit = repo.Lookup<Commit>(commitSha);

        if (commit == null)
        {
            throw new ArgumentException($"Commit '{commitSha}' not found in the repository.");
        }

        var changes = new Dictionary<string, FileChangeInfo>();

        try
        {
            if (!commit.Parents.Any())
            {
                // Root commit - all files are added
                var allFiles = GetAllFilesInTree(commit.Tree);
                foreach (var file in allFiles)
                {
                    changes[file] = new FileChangeInfo
                    {
                        Path = file,
                        ChangeType = ChangeKind.Added,
                        LinesAdded = GetFileLineCount(repo, commit, file),
                        LinesDeleted = 0
                    };
                }
                return changes;
            }

            var parent = commit.Parents.First();
            var patch = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);

            foreach (var patchEntry in patch)
            {
                changes[patchEntry.Path] = new FileChangeInfo
                {
                    Path = patchEntry.Path,
                    OldPath = patchEntry.OldPath,
                    ChangeType = patchEntry.Status,
                    LinesAdded = patchEntry.LinesAdded,
                    LinesDeleted = patchEntry.LinesDeleted
                };
            }
        }
        catch (LibGit2SharpException)
        {
            // Handle any Git errors gracefully
        }

        return changes;
    }

    /// <summary>
    /// Get the number of lines in a file at a specific commit
    /// </summary>
    private int GetFileLineCount(Repository repo, Commit commit, string filePath)
    {
        try
        {
            var blob = commit[filePath]?.Target as Blob;
            if (blob == null) return 0;

            using var reader = new StreamReader(blob.GetContentStream());
            var content = reader.ReadToEnd();
            return content.Split('\n').Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Additional utility: Get commits that modified a specific file
    /// Useful for cherry-pick analysis when you want to find all commits that touched a file
    /// </summary>
    public List<string> GetCommitsThatModifiedFile(string repositoryPath, string filePath, string branchName)
    {
        using var repo = new Repository(repositoryPath);
        var branch = repo.Branches[branchName];

        if (branch == null)
        {
            throw new ArgumentException($"Branch '{branchName}' not found in the repository.");
        }

        var filter = new CommitFilter
        {
            IncludeReachableFrom = branch,
            SortBy = CommitSortStrategies.Time
        };

        var commitsModifyingFile = new List<string>();

        foreach (var commit in repo.Commits.QueryBy(filter))
        {
            if (!commit.Parents.Any())
            {
                // Root commit - check if file exists
                if (commit[filePath] != null)
                {
                    commitsModifyingFile.Add(commit.Sha);
                }
                continue;
            }

            try
            {
                var parent = commit.Parents.First();
                var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);

                if (changes.Any(c => c.Path == filePath || c.OldPath == filePath))
                {
                    commitsModifyingFile.Add(commit.Sha);
                }
            }
            catch (LibGit2SharpException)
            {
                // Skip commits that can't be processed
            }
        }

        return commitsModifyingFile;
    }
}

/// <summary>
/// Detailed information about changes to a specific file in a commit
/// </summary>
public class FileChangeInfo
{
    public string Path { get; set; } = string.Empty;
    public string? OldPath { get; set; }
    public ChangeKind ChangeType { get; set; }
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }

    /// <summary>
    /// Total number of lines changed (added + deleted)
    /// </summary>
    public int TotalLinesChanged => LinesAdded + LinesDeleted;

    /// <summary>
    /// Net line change (added - deleted)
    /// </summary>
    public int NetLineChange => LinesAdded - LinesDeleted;
}