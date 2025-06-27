using CherryPickSmart.Models;

namespace CherryPickSmart.Core.GitAnalysis;

public class MergeCommitAnalyzer
{
    public record MergeAnalysis
    {
        public string MergeSha { get; init; } = "";
        public HashSet<string> IntroducedCommits { get; init; } = new();
        public bool IsCompleteInTarget { get; init; }
        public List<string> MissingCommits { get; init; } = new();
    }

    public List<MergeAnalysis> AnalyzeMerges(CommitGraph graph, HashSet<string> targetBranchCommits)
    {
        var mergeAnalyses = new List<MergeAnalysis>();

        // Find all merge commits
        var mergeCommits = graph.Commits.Values.Where(c => c.IsMergeCommit);

        foreach (var merge in mergeCommits)
        {
            // Get commits introduced by this merge
            var mergeDescendants = GetDescendants(graph, merge.Sha);
            var firstParentDescendants = merge.ParentShas.Any()
                ? GetDescendants(graph, merge.ParentShas[0], firstParentOnly: true)
                : new HashSet<string>();

            var introducedCommits = mergeDescendants.Except(firstParentDescendants).ToHashSet();

            // Check if all introduced commits exist in target branch
            var missingCommits = introducedCommits.Except(targetBranchCommits).ToList();
            var isComplete = !missingCommits.Any();

            mergeAnalyses.Add(new MergeAnalysis
            {
                MergeSha = merge.Sha,
                IntroducedCommits = introducedCommits,
                IsCompleteInTarget = isComplete,
                MissingCommits = missingCommits
            });
        }

        return mergeAnalyses;
    }

    private HashSet<string> GetDescendants(CommitGraph graph, string startSha, bool firstParentOnly = false)
    {
        var descendants = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(startSha);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!descendants.Add(current))
                continue; // Already visited

            if (graph.ChildrenMap.TryGetValue(current, out var children))
            {
                if (firstParentOnly && children.Any())
                {
                    var firstParentChild = children.FirstOrDefault(childSha =>
                    {
                        var child = graph.Commits[childSha];
                        return child.ParentShas.FirstOrDefault() == current;
                    });

                    if (firstParentChild != null)
                        queue.Enqueue(firstParentChild);
                }
                else
                {
                    foreach (var child in children)
                        queue.Enqueue(child);
                }
            }
        }

        return descendants;
    }
}
