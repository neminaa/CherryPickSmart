using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.GitAnalysis;

/// <summary>
/// Detects commits that would result in empty cherry-picks
/// </summary>
public class EmptyCommitDetector
{
    private readonly string _repositoryPath;

    public EmptyCommitDetector(string repositoryPath)
    {
        _repositoryPath = repositoryPath;
    }

    /// <summary>
    /// Detects which commits would be empty when cherry-picked to the target branch
    /// </summary>
    public Dictionary<string, EmptyCommitInfo> DetectEmptyCommits(
        List<CpCommit> commits, 
        string targetBranch)
    {
        var emptyCommits = new Dictionary<string, EmptyCommitInfo>();
        
        using var repo = new Repository(_repositoryPath);
        var targetBranchRef = repo.Branches[targetBranch];
        
        if (targetBranchRef == null)
        {
            throw new ArgumentException($"Target branch '{targetBranch}' not found");
        }

        foreach (var commit in commits)
        {
            var emptyInfo = CheckIfCommitWouldBeEmpty(repo, commit, targetBranchRef);
            if (emptyInfo != null)
            {
                emptyCommits[commit.Sha] = emptyInfo;
            }
        }

        return emptyCommits;
    }

    private EmptyCommitInfo? CheckIfCommitWouldBeEmpty(
        Repository repo, 
        CpCommit cpCommit, 
        Branch targetBranch)
    {
        try
        {
            
            var commit = repo.Lookup<Commit>(cpCommit.Sha);
            if (commit == null) return null;

            // For merge commits, check if all changes are already in target
            if (cpCommit.IsMergeCommit)
            {
                return CheckMergeCommitEmpty(repo, commit, targetBranch);
            }

            // For regular commits, check if the exact changes exist
            return CheckRegularCommitEmpty(repo, commit, targetBranch);
        }
        catch (LibGit2SharpException)
        {
            // If we can't analyze the commit, assume it's not empty
            return null;
        }
    }

    private EmptyCommitInfo? CheckRegularCommitEmpty(
        Repository repo, 
        Commit commit, 
        Branch targetBranch)
    {
        if (!commit.Parents.Any())
        {
            // Root commit - check if all files already exist with same content
            return CheckRootCommitEmpty(repo, commit, targetBranch);
        }

        var parent = commit.Parents.First();
        var sourceChanges = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);
        
        // Get all commits in target branch that might contain these changes
        var targetCommits = GetTargetBranchCommits(repo, targetBranch);
        var modifiedFiles = sourceChanges.Select(c => c.Path).ToHashSet();

        // Check if all changes from this commit already exist in target
        var allChangesFound = true;
        var reasonParts = new List<string>();

        foreach (var change in sourceChanges)
        {
            var isChangeInTarget = IsChangeAlreadyInTarget(repo, change, commit, targetBranch.Tip);
            
            if (!isChangeInTarget)
            {
                allChangesFound = false;
                break;
            }
            else
            {
                reasonParts.Add($"{change.Path} already has these changes");
            }
        }

        if (allChangesFound && sourceChanges.Any())
        {
            return new EmptyCommitInfo
            {
                CommitSha = commit.Sha,
                Reason = EmptyReason.ChangesAlreadyApplied,
                Details = $"All changes already exist in target branch: {string.Join(", ", reasonParts.Take(3))}" +
                         (reasonParts.Count > 3 ? $" and {reasonParts.Count - 3} more files" : "")
            };
        }

        // Check if this is a revert that undoes changes not in target
        if (IsRevertOfNonExistentChanges(repo, commit, targetBranch))
        {
            return new EmptyCommitInfo
            {
                CommitSha = commit.Sha,
                Reason = EmptyReason.RevertOfNonExistentChanges,
                Details = "This is a revert of changes that don't exist in the target branch"
            };
        }

        return null;
    }

    private EmptyCommitInfo? CheckMergeCommitEmpty(
        Repository repo, 
        Commit mergeCommit, 
        Branch targetBranch)
    {
        // For merge commits, we need to check if the merged changes are already in target
        if (mergeCommit.Parents.Count() < 2)
        {
            return null; // Not a merge commit
        }
        
        // Compare merge commit with its first parent (mainline)
        var mainlineParent = mergeCommit.Parents.First();
        var changes = repo.Diff.Compare<TreeChanges>(mainlineParent.Tree, mergeCommit.Tree);

        if (!changes.Any())
        {
            // This is a no-op merge (no actual changes)
            return new EmptyCommitInfo
            {
                CommitSha = mergeCommit.Sha,
                Reason = EmptyReason.NoOpMerge,
                Details = "Merge commit contains no actual file changes"
            };
        }

        // Check if all merged changes already exist in target
        var allChangesInTarget = true;
        foreach (var change in changes)
        {
            if (!IsChangeAlreadyInTarget(repo, change, mergeCommit, targetBranch.Tip))
            {
                allChangesInTarget = false;
                break;
            }
        }

        if (allChangesInTarget)
        {
            return new EmptyCommitInfo
            {
                CommitSha = mergeCommit.Sha,
                Reason = EmptyReason.MergedChangesAlreadyPresent,
                Details = "All changes from this merge already exist in the target branch"
            };
        }

        return null;
    }

    private EmptyCommitInfo? CheckRootCommitEmpty(
        Repository repo, 
        Commit rootCommit, 
        Branch targetBranch)
    {
        // For root commits, check if all files already exist with the same content
        var allFilesExist = true;
        
        foreach (var entry in rootCommit.Tree)
        {
            var targetEntry = targetBranch.Tip[entry.Path];
            if (targetEntry == null || targetEntry.Target.Sha != entry.Target.Sha)
            {
                allFilesExist = false;
                break;
            }
        }

        if (allFilesExist)
        {
            return new EmptyCommitInfo
            {
                CommitSha = rootCommit.Sha,
                Reason = EmptyReason.ChangesAlreadyApplied,
                Details = "All files from root commit already exist in target with same content"
            };
        }

        return null;
    }

    private bool IsChangeAlreadyInTarget(
        Repository repo, 
        TreeEntryChanges change, 
        Commit sourceCommit, 
        Commit targetCommit)
    {
        try
        {
            // For deleted files, check if file doesn't exist in target
            if (change.Status == ChangeKind.Deleted)
            {
                return targetCommit[change.Path] == null;
            }

            // For added/modified files, check if content matches
            var sourceEntry = sourceCommit[change.Path];
            var targetEntry = targetCommit[change.Path];

            if (sourceEntry == null || targetEntry == null)
            {
                return false;
            }

            // Compare content by SHA
            return sourceEntry.Target.Sha == targetEntry.Target.Sha;
        }
        catch
        {
            return false;
        }
    }

    private bool IsRevertOfNonExistentChanges(
        Repository repo, 
        Commit commit, 
        Branch targetBranch)
    {
        // Check if commit message indicates a revert
        if (!commit.Message.StartsWith("Revert", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Extract the reverted commit SHA from the message if possible
        var revertedShaMatch = System.Text.RegularExpressions.Regex.Match(
            commit.Message, 
            @"This reverts commit ([a-f0-9]{40})", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!revertedShaMatch.Success)
        {
            return false;
        }

        var revertedSha = revertedShaMatch.Groups[1].Value;
        
        // Check if the reverted commit exists in target branch
        var targetCommits = GetTargetBranchCommits(repo, targetBranch);
        return !targetCommits.Contains(revertedSha);
    }

    private HashSet<string> GetTargetBranchCommits(Repository repo, Branch branch)
    {
        var filter = new CommitFilter
        {
            IncludeReachableFrom = branch,
            SortBy = CommitSortStrategies.Time
        };

        return repo.Commits.QueryBy(filter).Select(c => c.Sha).ToHashSet();
    }

    /// <summary>
    /// Checks if a specific commit is effectively empty by attempting a dry-run merge
    /// </summary>
    public bool IsCommitEffectivelyEmpty(string commitSha, string targetBranch)
    {
        using var repo = new Repository(_repositoryPath);
        
        try
        {
            var commit = repo.Lookup<Commit>(commitSha);
            var target = repo.Branches[targetBranch];
            
            if (commit == null || target == null)
            {
                return false;
            }

            // Create a temporary branch for testing
            var tempBranchName = $"temp-empty-check-{Guid.NewGuid():N}";
            var tempBranch = repo.CreateBranch(tempBranchName, target.Tip);
            
            try
            {
                LibGit2Sharp.Commands.Checkout(repo, tempBranch);
                
                // Try to cherry-pick
                var options = new CherryPickOptions
                {
                    MergeFileFavor = MergeFileFavor.Theirs
                };
                
                var result = repo.CherryPick(commit, commit.Committer, options);
                
                // Check if the result would be empty
                var status = repo.RetrieveStatus();
                var isEmpty = result.Status == CherryPickStatus.CherryPicked && 
                             !status.Any();
                
                return isEmpty;
            }
            finally
            {
                // Clean up
                LibGit2Sharp.Commands.Checkout(repo, target);
                repo.Branches.Remove(tempBranch);
            }
        }
        catch
        {
            // If we can't determine, assume not empty
            return false;
        }
    }
}

public class EmptyCommitInfo
{
    public string CommitSha { get; set; } = "";
    public EmptyReason Reason { get; set; }
    public string Details { get; set; } = "";
}

public enum EmptyReason
{
    Unknown,
    ChangesAlreadyApplied,
    NoOpMerge,
    MergedChangesAlreadyPresent,
    RevertOfNonExistentChanges,
    ConflictResolutionOnly
}
