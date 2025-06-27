using CherryPickSmart.Models;
using LibGit2Sharp;

namespace CherryPickSmart.Core.GitAnalysis;

public class MergeCommitAnalyzer
{
    public record MergeAnalysis
    {
        public string MergeSha { get; init; } = "";
        public HashSet<string> IntroducedCommits { get; init; } = [];
        public string FirstParent { get; init; } = "";
        public string SecondParent { get; init; } = "";

        // Cherry-pick strategy analysis
        public bool CanCherryPickMerge { get; init; }
        public bool ShouldCherryPickIndividually { get; init; }
        public MergeCherryPickStrategy RecommendedStrategy { get; init; }
        public string StrategyReason { get; init; } = "";
        public List<string> MissingCommits { get; init; } = [];
        public required string Message { get; set; }
    }

    public enum MergeCherryPickStrategy
    {
        CherryPickMergeCommit,      // Cherry-pick the merge commit itself (git cherry-pick -m 1 <merge>)
        CherryPickIndividually,     // Cherry-pick each commit separately  
        PartialCherryPick,          // Some commits already exist, cherry-pick the rest
        AlreadyApplied,             // All changes already in target
        ConflictRisk                // High risk of conflicts, manual intervention needed
    }

    /// <summary>
    /// Analyze merge commits to determine optimal cherry-pick strategy
    /// </summary>
    public List<MergeAnalysis> AnalyzeMerges(CpCommitGraph graph, HashSet<string> targetBranchCommits)
    {
        var mergeAnalyses = new List<MergeAnalysis>();

        // Find all merge commits (commits with more than one parent)
        var mergeCommits = graph.Commits.Values.Where(c => c.ParentShas.Count > 1);

        foreach (var merge in mergeCommits)
        {
            if (merge.ParentShas.Count < 2)
                continue; // Skip if somehow not a real merge

            var firstParent = merge.ParentShas[0];   // Usually the target branch (main/develop)
            var secondParent = merge.ParentShas[1];  // Usually the feature branch being merged

            // Get commits introduced by this merge
            var introducedCommits = GetCommitsIntroducedByMerge(graph, firstParent, secondParent);

            // Analyze cherry-pick strategy
            var strategy = DetermineCherryPickStrategy(
                firstParent,
                introducedCommits,
                targetBranchCommits,
                graph);

            mergeAnalyses.Add(new MergeAnalysis
            {
                Message = merge.Message,
                MergeSha = merge.Sha,
                FirstParent = firstParent,
                SecondParent = secondParent,
                IntroducedCommits = introducedCommits,
                CanCherryPickMerge = strategy.CanCherryPickMerge,
                ShouldCherryPickIndividually = strategy.ShouldCherryPickIndividually,
                RecommendedStrategy = strategy.Strategy,
                StrategyReason = strategy.Reason,
                MissingCommits = strategy.MissingCommits
            });
        }

        return mergeAnalyses;
    }

    /// <summary>
    /// Determine the optimal cherry-pick strategy for a merge commit
    /// </summary>
    private (bool CanCherryPickMerge, bool ShouldCherryPickIndividually,
             MergeCherryPickStrategy Strategy, string Reason, List<string> MissingCommits)
        DetermineCherryPickStrategy(string firstParent, HashSet<string> introducedCommits,
                                   HashSet<string> targetBranchCommits, CpCommitGraph graph)
    {
        var missingCommits = introducedCommits.Except(targetBranchCommits).ToList();
        var existingCommits = introducedCommits.Intersect(targetBranchCommits).ToList();

        // Check if first parent exists in target (required for merge cherry-pick)
        var firstParentExistsInTarget = targetBranchCommits.Contains(firstParent) ||
                                       HasEquivalentCommitInTarget(firstParent, targetBranchCommits, graph);

        // Strategy decision logic
        if (missingCommits.Count == 0)
        {
            return (false, false, MergeCherryPickStrategy.AlreadyApplied,
                   "All commits from this merge already exist in target", []);
        }

        if (!firstParentExistsInTarget)
        {
            return (false, true, MergeCherryPickStrategy.ConflictRisk,
                   $"First parent {firstParent[..7]} not found in target - merge cherry-pick not possible",
                   missingCommits);
        }

        if (existingCommits.Count == 0)
        {
            // All introduced commits are missing - perfect for merge cherry-pick
            return (true, false, MergeCherryPickStrategy.CherryPickMergeCommit,
                   "All introduced commits are missing - merge cherry-pick is ideal",
                   missingCommits);
        }

        if (missingCommits.Count < introducedCommits.Count * 0.5)
        {
            // Less than 50% missing - individual cherry-pick might be better
            return (true, true, MergeCherryPickStrategy.PartialCherryPick,
                   $"{existingCommits.Count} commits already exist, {missingCommits.Count} missing - consider individual cherry-pick",
                   missingCommits);
        }

        // Most commits are missing - merge cherry-pick is still good
        return (true, false, MergeCherryPickStrategy.CherryPickMergeCommit,
               $"Most commits missing ({missingCommits.Count}/{introducedCommits.Count}) - merge cherry-pick recommended",
               missingCommits);
    }

    /// <summary>
    /// Check if a commit has an equivalent in the target branch (for cherry-pick detection)
    /// This is a simplified version - you'd want to implement proper patch-id or content comparison
    /// </summary>
    private bool HasEquivalentCommitInTarget(string commitSha, HashSet<string> targetBranchCommits, CpCommitGraph graph)
    {
        // This is a placeholder - implement your cherry-pick detection logic here
        // Could compare:
        // - Patch IDs
        // - Commit messages + author + timestamp
        // - File changes

        if (!graph.Commits.TryGetValue(commitSha, out var commit))
            return false;

        // For now, simple check - you'd replace this with sophisticated matching
        return targetBranchCommits.Any(targetSha =>
        {
            if (!graph.Commits.TryGetValue(targetSha, out var targetCommit))
                return false;

            // Simple similarity check (replace with your actual logic)
            return commit.Message == targetCommit.Message &&
                   commit.Author == targetCommit.Author &&
                   Math.Abs((commit.Timestamp - targetCommit.Timestamp).TotalHours) < 24;
        });
    }

    /// <summary>
    /// Get commits that were introduced by a merge (commits in second parent but not in first parent)
    /// This is equivalent to: git rev-list secondParent ^firstParent
    /// </summary>
    private HashSet<string> GetCommitsIntroducedByMerge(CpCommitGraph graph, string firstParent, string secondParent)
    {
        // Get all commits reachable from second parent
        var reachableFromSecond = GetAncestors(graph, secondParent);

        // Get all commits reachable from first parent  
        var reachableFromFirst = GetAncestors(graph, firstParent);

        // Commits introduced = reachable from second but not from first
        return reachableFromSecond.Except(reachableFromFirst).ToHashSet();
    }

    /// <summary>
    /// Get all ancestor commits (going backwards through parents) from a starting commit
    /// This is the reverse of GetDescendants - we go UP the tree, not down
    /// </summary>
    private HashSet<string> GetAncestors(CpCommitGraph graph, string startSha)
    {
        var ancestors = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(startSha);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (!ancestors.Add(current))
                continue; // Already visited

            // Get parents of current commit and add them to queue
            if (graph.Commits.TryGetValue(current, out var commit))
            {
                foreach (var parent in commit.ParentShas)
                {
                    queue.Enqueue(parent);
                }
            }
        }

        return ancestors;
    }

   
    /// <summary>
    /// Get actionable cherry-pick recommendations based on merge analysis
    /// </summary>
    public List<CherryPickRecommendation> GetCherryPickRecommendations(List<MergeAnalysis> mergeAnalyses)
    {
        var recommendations = new List<CherryPickRecommendation>();

        foreach (var analysis in mergeAnalyses)
        {
            switch (analysis.RecommendedStrategy)
            {
                case MergeCherryPickStrategy.CherryPickMergeCommit:
                    recommendations.Add(new CherryPickRecommendation
                    {
                        Type = "merge",
                        Command = $"git cherry-pick -m 1 {analysis.MergeSha}",
                        Description = $"Cherry-pick merge commit {analysis.MergeSha[..7]} as single operation",
                        Reason = analysis.StrategyReason,
                        Priority = 1 // High priority - clean merge
                    });
                    break;

                case MergeCherryPickStrategy.CherryPickIndividually:
                case MergeCherryPickStrategy.PartialCherryPick:
                    foreach (var commitSha in analysis.MissingCommits)
                    {
                        recommendations.Add(new CherryPickRecommendation
                        {
                            Type = "individual",
                            Command = $"git cherry-pick {commitSha}",
                            Description = $"Cherry-pick individual commit {commitSha[..7]}",
                            Reason = analysis.StrategyReason,
                            Priority = 2 // Medium priority - individual picks
                        });
                    }
                    break;

                case MergeCherryPickStrategy.ConflictRisk:
                    recommendations.Add(new CherryPickRecommendation
                    {
                        Type = "manual",
                        Command = $"# Manual intervention needed for {analysis.MergeSha[..7]}",
                        Description = $"Manual review required for merge {analysis.MergeSha[..7]}",
                        Reason = analysis.StrategyReason,
                        Priority = 3 // Low priority - needs manual review
                    });
                    break;

                case MergeCherryPickStrategy.AlreadyApplied:
                    // No action needed
                    break;
            }
        }

        return recommendations.OrderBy(r => r.Priority).ToList();
    }

    public record CherryPickRecommendation
    {
        public string Type { get; init; } = ""; // "merge", "individual", "manual"
        public string Command { get; init; } = "";
        public string Description { get; init; } = "";
        public string Reason { get; init; } = "";
        public int Priority { get; init; } // 1 = high, 2 = medium, 3 = low
    }

    /// <summary>
    /// Get all merge commits from a collection
    /// </summary>
    public List<CpCommit> GetMergeCommits(IEnumerable<CpCommit> commits)
    {
        return commits.Where(c => c.ParentShas.Count > 1).ToList();
    }

    /// <summary>
    /// Get the most recent commits
    /// </summary>
    public List<CpCommit> GetRecentCommits(IEnumerable<CpCommit> commits, int count = 1)
    {
        return commits.OrderByDescending(c => c.Timestamp).Take(count).ToList();
    }

    /// <summary>
    /// Get all merge commits from LibGit2Sharp commits
    /// </summary>
    public List<Commit> GetLibGit2SharpMergeCommits(IEnumerable<Commit> commits)
    {
        return commits.Where(c => c.Parents.Count() > 1).ToList();
    }

    /// <summary>
    /// Get the most recent LibGit2Sharp commits
    /// </summary>
    public List<Commit> GetRecentLibGit2SharpCommits(IEnumerable<Commit> commits, int count = 1)
    {
        return commits.OrderByDescending(c => c.Author.When).Take(count).ToList();
    }
}