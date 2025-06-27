using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.TicketAnalysis;

public interface ITicketInferenceEngine
{
    /// <summary>
    /// Groups commits by ticket.
    /// </summary>
    Dictionary<string, List<CpCommit>> GroupByTicket(IEnumerable<CpCommit> commits);
}