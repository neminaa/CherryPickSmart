using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.ConflictAnalysis;

public interface IOrderOptimizer
{
    /// <summary>
    /// Computes the pick order for commits.
    /// </summary>
    List<CpCommit> ComputePickOrder(
        IEnumerable<CpCommit> mergeCommits,
        IEnumerable<CpCommit> lastCommits,
        IEnumerable<CpCommit> otherCommits);
}