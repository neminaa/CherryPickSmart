using System.Text.RegularExpressions;
using CherryPickSmart.Models;

namespace CherryPickSmart.Core.TicketAnalysis;

public class OrphanCommitDetector
{
    public record OrphanCommit
    {
        public Commit Commit { get; init; } = null!;
        public List<TicketSuggestion> Suggestions { get; init; } = new();
        public string Reason { get; init; } = ""; // Why it's orphaned
    }

    public record TicketSuggestion
    {
        public string TicketKey { get; init; } = "";
        public double Confidence { get; init; } // 0-100
        public List<string> Reasons { get; init; } = new();
    }

    public List<OrphanCommit> FindOrphans(
        CommitGraph graph, 
        Dictionary<string, List<string>> ticketCommitMap)
    {
        var orphans = new List<OrphanCommit>();
        var allCommitsWithTickets = ticketCommitMap.Values.SelectMany(x => x).ToHashSet();

        foreach (var (sha, commit) in graph.Commits)
        {
            if (!allCommitsWithTickets.Contains(sha))
            {
                var reason = DetermineOrphanReason(commit);
                orphans.Add(new OrphanCommit
                {
                    Commit = commit,
                    Reason = reason,
                    Suggestions = new() // Will be filled by inference engine
                });
            }
        }

        return orphans;
    }

    private string DetermineOrphanReason(Commit commit)
    {
        if (Regex.IsMatch(commit.Message, @"(?i)(hsamed|proj)\s*\d+"))
            return "Malformed ticket reference detected";

        if (commit.Message.Length < 10)
            return "Commit message too short";

        return "No ticket reference found";
    }
}
