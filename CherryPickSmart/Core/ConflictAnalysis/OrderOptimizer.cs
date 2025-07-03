using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Models;

namespace CherryPickSmart.Core.ConflictAnalysis;

public class OrderOptimizer
{
    public List<CherryPickStep> OptimizeOrder(List<CpCommit> selectedCommits,
        List<MergeCommitAnalyzer.MergeAnalysis> completeMerges,
        List<ConflictPrediction> conflicts, string srcBranch)
    {
        var steps = new List<CherryPickStep>();
        var processedCommits = new HashSet<string>();

        foreach (var merge in completeMerges)
        {
            var mergeCommitShas = merge.IntroducedCommits
                .Where(sha => selectedCommits.Any(c => c.Sha == sha))
                .ToList();

            if (mergeCommitShas.Count > 0)
            {
                var desc = merge.Message
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .First();
                
                desc = desc.Replace("[", string.Empty);
                desc = desc.Replace("]", string.Empty);
                //desc = desc.Replace("/", string.Empty);

                var step = new CherryPickStep
                {
                    Type = StepType.MergeCommit,
                    CommitShas = [merge.MergeSha],
                    //Description = $"Preserve merge: {desc}",
                    Description = $"Preserve merge {desc}",
                    GitCommand = $"git cherry-pick -m 1 {merge.ShortMergeSha}",
                };
                
                var isEmpty = !string.IsNullOrEmpty(merge.TargetBranch) &&
                              !string.Equals(merge.TargetBranch, srcBranch,
                                  StringComparison.InvariantCultureIgnoreCase);

                if (isEmpty)
                {
                    step.IsEmpty = true;
                    step.EmptyReason = $"Target branch is not {srcBranch}";
                    step.AlternativeCommand =
                        $"git cherry-pick -m 1 --strategy recursive -X ours {merge.ShortMergeSha} --allow-empty # {step.EmptyReason}";
                }

                steps.Add(step);

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
                    var desc = range[0].Message
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .First();
                    steps.Add(new CherryPickStep
                    {
                        Type = StepType.SingleCommit,
                        CommitShas = [range[0].Sha],
                        Description = $"{group.Key}: {desc}",
                        GitCommand = $"git cherry-pick {range[0].ShortSha}"
                    });
                }
                else
                {
                    steps.Add(new CherryPickStep
                    {
                        Type = StepType.CommitRange,
                        CommitShas = range.Select(c => c.Sha).ToList(),
                        Description = $"{group.Key}: {range.Count} commits",
                        GitCommand = $"git cherry-pick {range.First().ShortSha}^..{range.Last().ShortSha}"
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

    private static List<List<CpCommit>> FindConsecutiveRanges(List<CpCommit> commits)
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
    
    // New properties for empty commit handling
    public bool IsEmpty { get; set; }
    public string? EmptyReason { get; set; }
    public string? AlternativeCommand { get; set; }
}

public enum StepType { SingleCommit, MergeCommit, CommitRange }
