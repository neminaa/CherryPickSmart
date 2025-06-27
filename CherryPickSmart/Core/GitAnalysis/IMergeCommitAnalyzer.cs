using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.GitAnalysis;

public interface IMergeCommitAnalyzer
{
    /// <summary>
    /// Gets merge commits from a list of commits.
    /// </summary>
    List<CpCommit> GetMergeCommits(IEnumerable<CpCommit> commits);

    /// <summary>
    /// Gets the last commits in a list of commits.
    /// </summary>
    List<CpCommit> GetLastCommits(IEnumerable<CpCommit> commits);
}