using CherryPickSmart.Models;
using static CherryPickSmart.Core.GitAnalysis.MergeCommitAnalyzer;
using static CherryPickSmart.Core.TicketAnalysis.OrphanCommitDetector;

namespace CherryPickSmart.Core.TicketAnalysis;

public class TicketInferenceEngine
{
    public async Task<List<TicketSuggestion>> GenerateSuggestionsAsync(
        List<MergeAnalysis> analysis,
        OrphanCommit orphan,
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();

        suggestions.AddRange(await AnalyzeMergeContextAsync(analysis, orphan, graph));
        //suggestions.AddRange(AnalyzeTemporalClusteringAsync(orphan, graph).Result);
        //suggestions.AddRange(AnalyzeFileOverlapAsync(orphan, graph, ticketCommitMap).Result);

        return RankSuggestions(suggestions);
    }

    private static Task<List<TicketSuggestion>> AnalyzeMergeContextAsync(
        List<MergeAnalysis> analysis,
        OrphanCommit orphan,
        CpCommitGraph graph)
    {
        var suggestions = new List<TicketSuggestion>();

        foreach (var (mergeSha, merge) in graph.Commits.Where(c => c.Value.IsMergeCommit))
        {

            var mergeAnalysis = analysis.FirstOrDefault(a => a.MergeSha == mergeSha);
            if (mergeAnalysis?.IntroducedCommits.Contains(orphan.Commit.Sha) == true)
            {
                var ticketCounts = new Dictionary<string, int>();

                foreach (var introducedSha in mergeAnalysis.IntroducedCommits)
                {
                    if (graph.Commits.TryGetValue(introducedSha,out var val) && val.ExtractedTickets.Count > 0)
                    {
                        foreach (var ticket in graph.Commits[introducedSha].ExtractedTickets)
                        {
                            ticketCounts[ticket] = ticketCounts.GetValueOrDefault(ticket) + 1;
                        }
                    }
                }

                if (ticketCounts.Count > 0)
                {
                    var dominantTicket = ticketCounts.OrderByDescending(x => x.Value).First();
                    var totalCommitsInMerge = mergeAnalysis.IntroducedCommits.Count;
                    var confidence = dominantTicket.Value / (double)totalCommitsInMerge * 100;

                    suggestions.Add(new TicketSuggestion
                    {
                        TicketKey = dominantTicket.Key,
                        Confidence = Math.Min(95, confidence + 20),
                        Reasons = ["merge_context", $"part_of_merge_{merge.ShortSha}"]
                    });
                }
            }
        }

        return Task.FromResult(suggestions);
    }

    private static Task<List<TicketSuggestion>> AnalyzeTemporalClusteringAsync(
        OrphanCommit orphan,
        CpCommitGraph graph)
    {
        var suggestions = new List<TicketSuggestion>();
        var timeWindow = TimeSpan.FromHours(4);

        var nearbyCommits = graph.Commits.Values
            .Where(c => c.Author == orphan.Commit.Author)
            .Where(c => Math.Abs((c.Timestamp - orphan.Commit.Timestamp).TotalHours) <= timeWindow.TotalHours)
            .Where(c => c.ExtractedTickets.Count != 0)
            .ToList();

        var ticketScores = new Dictionary<string, double>();

        foreach (var nearby in nearbyCommits)
        {
            var timeDiff = Math.Abs((nearby.Timestamp - orphan.Commit.Timestamp).TotalMinutes);
            var weight = 1.0 / (1.0 + timeDiff / 60.0);

            foreach (var ticket in nearby.ExtractedTickets)
            {
                ticketScores[ticket] = ticketScores.GetValueOrDefault(ticket) + weight;
            }
        }

        foreach (var (ticket, score) in ticketScores.OrderByDescending(x => x.Value).Take(3))
        {
            var confidence = Math.Min(70, score * 40);
            suggestions.Add(new TicketSuggestion
            {
                TicketKey = ticket,
                Confidence = confidence,
                Reasons = ["temporal_clustering", $"by_{orphan.Commit.Author}"]
            });
        }

        return Task.FromResult(suggestions);
    }

    private Task<List<TicketSuggestion>> AnalyzeFileOverlapAsync(
        OrphanCommit orphan,
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();

        var fileOverlaps = new Dictionary<string, List<string>>();

        foreach (var (ticket, commitShas) in ticketCommitMap)
        {
            var overlappingFiles = new HashSet<string>();

            foreach (var cpCommit in commitShas)
            {
                if (graph.Commits.TryGetValue(cpCommit.Sha, out var commit))
                {
                    var commonFiles = commit.ModifiedFiles.Intersect(orphan.Commit.ModifiedFiles);
                    overlappingFiles.UnionWith(commonFiles);
                }
            }

            if (overlappingFiles.Count > 0)
            {
                fileOverlaps[ticket] = overlappingFiles.ToList();
            }
        }

        foreach (var (ticket, overlappingFiles) in fileOverlaps.OrderByDescending(x => x.Value.Count).Take(3))
        {
            var overlapRatio = overlappingFiles.Count / (double)orphan.Commit.ModifiedFiles.Count;
            var confidence = Math.Min(60, overlapRatio * 80);

            suggestions.Add(new TicketSuggestion
            {
                TicketKey = ticket,
                Confidence = confidence,
                Reasons = ["file_overlap", $"{overlappingFiles.Count}_common_files"]
            });
        }

        return Task.FromResult(suggestions);
    }

    private List<TicketSuggestion> RankSuggestions(List<TicketSuggestion> suggestions)
    {
        var aggregated = suggestions
            .GroupBy(s => s.TicketKey)
            .Select(g => new TicketSuggestion
            {
                TicketKey = g.Key,
                Confidence = g.Max(s => s.Confidence),
                Reasons = g.SelectMany(s => s.Reasons).Distinct().ToList()
            })
            .OrderByDescending(s => s.Confidence)
            .ToList();

        return aggregated;
    }
}
