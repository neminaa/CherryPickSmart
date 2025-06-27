using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.TicketAnalysis;

public interface IOrphanCommitDetector
{
    /// <summary>
    /// Detects orphan commits from a list of commits.
    /// </summary>
    List<CpCommit> Detect(IEnumerable<CpCommit> commits);
}