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

            // Collect modified files
            var modifiedFiles = new List<string>();
            foreach (var entry in commit.Tree)
            {
                modifiedFiles.Add(entry.Path);
            }

            commits[sha] = new CpCommit(commit,modifiedFiles);

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

    public HashSet<string> GetCommitsInBranch(string repositoryPath, string branchName)
    {
        using var repo = new Repository(repositoryPath);

        var branch = repo.Branches[branchName];
        if (branch == null)
        {
            throw new ArgumentException($"Branch '{branchName}' not found in the repository.");
        }

        return branch.Commits.Select(c => c.Sha).ToHashSet();
    }
}
