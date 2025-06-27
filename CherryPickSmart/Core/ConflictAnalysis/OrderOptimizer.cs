using CherryPickSmart.Commands;
using CherryPickSmart.Models;
using static CherryPickSmart.Core.ConflictAnalysis.ConflictPredictor;

namespace CherryPickSmart.Core.ConflictAnalysis;

public class OrderOptimizer
{
    public List<CherryPickStep> OptimizeOrder(
        List<CpCommit> selectedCommits,
        List<GitAnalysis.MergeCommitAnalyzer.MergeAnalysis> completeMerges,
        List<ConflictPredictor.ConflictPrediction> conflicts)
    {
        var steps = new List<CherryPickStep>();
        var processedCommits = new HashSet<string>();

        foreach (var merge in completeMerges)
        {
            var mergeCommitShas = merge.IntroducedCommits
                .Where(sha => selectedCommits.Any(c => c.Sha == sha))
                .ToList();

            if (mergeCommitShas.Any())
            {
                steps.Add(new CherryPickStep
                {
                    Type = StepType.MergeCommit,
                    CommitShas = [merge.MergeSha],
                    Description = $"Preserve merge {merge.MergeSha[..8]} with {mergeCommitShas.Count} commits",
                    GitCommand = $"git cherry-pick -m 1 {merge.MergeSha}"
                });

                processedCommits.UnionWith(mergeCommitShas);
            }
        }

        var ticketGroups = selectedCommits
            .Where(c => !processedCommits.Contains(c.Sha))
            .GroupBy(c => c.ExtractedTickets.FirstOrDefault() ?? c.InferredTicket ?? "NO_TICKET")
            .ToList();

        var orderedGroups = OrderTicketsByDependencyAndConflict(ticketGroups, conflicts);

        foreach (var group in orderedGroups)
        {
            var ticketCommits = group.OrderBy(c => c.Timestamp).ToList();
            var ranges = FindConsecutiveRanges(ticketCommits);

            foreach (var range in ranges)
            {
                if (range.Count == 1)
                {
                    steps.Add(new CherryPickStep
                    {
                        Type = StepType.SingleCommit,
                        CommitShas = [range[0].Sha],
                        Description = $"{group.Key}: {range[0].Message.Truncate(50)}",
                        GitCommand = $"git cherry-pick {range[0].Sha}"
                    });
                }
                else
                {
                    steps.Add(new CherryPickStep
                    {
                        Type = StepType.CommitRange,
                        CommitShas = range.Select(c => c.Sha).ToList(),
                        Description = $"{group.Key}: {range.Count} commits",
                        GitCommand = $"git cherry-pick {range.First().Sha}^..{range.Last().Sha}"
                    });
                }
            }
        }

        return steps;
    }

    private List<IGrouping<string, CpCommit>> OrderTicketsByDependencyAndConflict(
        List<IGrouping<string, CpCommit>> ticketGroups,
        List<ConflictPrediction> conflicts)
    {
        // Placeholder for ordering tickets by dependency and conflict
        return ticketGroups.OrderBy(g => g.Key).ToList();
    }

    private List<List<CpCommit>> FindConsecutiveRanges(List<CpCommit> commits)
    {
        // Placeholder for finding consecutive ranges of commits
        return commits.Select(c => new List<CpCommit> { c }).ToList();
    }
}

public record CherryPickStep
{
    public StepType Type { get; init; }
    public List<string> CommitShas { get; init; } = [];
    public string Description { get; init; } = "";
    public string GitCommand { get; init; } = "";
}

public enum StepType { SingleCommit, MergeCommit, CommitRange }
