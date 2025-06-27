using System.Text.RegularExpressions;
using CherryPickSmart.Models;

namespace CherryPickSmart.Core.TicketAnalysis;

public class OrphanCommitDetector
{
    public record OrphanCommit
    {
        public CpCommit Commit { get; init; } = null!;
        public List<TicketSuggestion> Suggestions { get; init; } = [];
        public string Reason { get; init; } = ""; // Why it's orphaned
    }

    public record TicketSuggestion
    {
        public string TicketKey { get; init; } = "";
        public double Confidence { get; init; } // 0-100
        public List<string> Reasons { get; init; } = [];
    }

    public List<OrphanCommit> FindOrphans(
        CpCommitGraph graph, 
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var orphans = new List<OrphanCommit>();
        var allCommitsWithTickets = ticketCommitMap.Values.SelectMany(x => x).ToHashSet();

        foreach (var (sha, commit) in graph.Commits)
        {
            if (allCommitsWithTickets.Any(a => a.Sha == sha)) continue;
            var reason = DetermineOrphanReason(commit);
            orphans.Add(new OrphanCommit
            {
                Commit = commit,
                Reason = reason,
                Suggestions = [] // Will be filled by inference engine
            });
        }

        return orphans;
    }

    private static string DetermineOrphanReason(CpCommit commit)
    {
        if (Regex.IsMatch(commit.Message, @"(?i)(hsamed|proj)\s*\d+"))
            return "Malformed ticket reference detected";

        return commit.Message.Length < 10 ? "Commit message too short" : "No ticket reference found";
    }
}
