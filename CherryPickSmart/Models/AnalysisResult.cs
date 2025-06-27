using CherryPickSmart.Core.ConflictAnalysis;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.TicketAnalysis;

namespace CherryPickSmart.Models
{
    /// <summary>
    /// Holds the complete outcome of a cherry-pick analysis between two branches.
    /// </summary>
    public class AnalysisResult
    {
        public required string FromBranch { get; set; }
        public required string ToBranch { get; set; }

        /// <summary>
        /// Grouped commits per ticket, with pick order and subcategories.
        /// </summary>
        public List<TicketGroup> Tickets { get; set; } = [];

        /// <summary>
        /// Commits that couldn't be associated with any ticket.
        /// </summary>
        public List<CpCommit> Orphans { get; set; } = [];
    }

    /// <summary>
    /// A bundle of commits related to a single ticket, categorized and ordered.
    /// </summary>

    public class TicketGroup
    {
        public required string Number { get; set; }
        public List<CpCommit> MergeCommits { get; set; } = [];
        public List<CpCommit> LastCommits { get; set; } = [];
        public List<CpCommit> OtherCommits { get; set; } = [];
        public List<CherryPickStep> PickOrder { get; set; } = [];
    }

    /// <summary>
    /// Builds an AnalysisResult by orchestrating the various engines.
    /// </summary>
    public static class AnalysisResultBuilder
    {
        public static AnalysisResult Build(string fromBranch,
            string toBranch,
            CpCommitGraph commitGraph,
            Dictionary<string, List<CpCommit>> ticketMap,
            List<MergeCommitAnalyzer.MergeAnalysis> mergeAnalyses,
            List<ConflictPredictor.ConflictPrediction> conflictPredictions,
            OrderOptimizer optimizer)
        {
            // Group commits by ticket (map of ticket -> commits)
            var allCommits = commitGraph.Commits.Values.ToList();
            
            // Detect orphans
            var orphans = allCommits.Where(w => w.IsOrpahan).ToList();

            var ticketGroups = new List<TicketGroup>();
            foreach (var (key, commitsForTicket) in ticketMap)
            {
                var group = new TicketGroup
                {
                    Number = key,
                    MergeCommits = commitsForTicket.Where(w => w.IsMergeCommit && w.ExtractedTickets.Contains(key)).ToList(),
                    LastCommits = commitsForTicket.Where(w => !w.IsMergeCommit && w.ExtractedTickets.Contains(key)).ToList()
                };

                // Other = those not in Merge or Last
                group.OtherCommits = commitsForTicket
                    .Except(group.MergeCommits)
                    .Except(group.LastCommits)
                    .ToList();

                // Filter mergeAnalyses for this group's MergeCommits
                var groupMergeAnalyses = mergeAnalyses
                    .Where(ma => group.MergeCommits.Any(mc => mc.Sha == ma.MergeSha))
                    .ToList();

                // Filter conflictPredictions for this group's LastCommits
                var groupConflictPredictions = conflictPredictions
                    .Where(cp => cp.ConflictingCommits.Any(cc => group.LastCommits.Any(lc => lc.Sha == cc.Sha)))
                    .ToList();


                // PickOrder: merge first, then last, then others
                group.PickOrder = optimizer.OptimizeOrder(group.MergeCommits, groupMergeAnalyses, groupConflictPredictions);

                ticketGroups.Add(group);
            }

            return new AnalysisResult
            {
                FromBranch = fromBranch,
                ToBranch = toBranch,
                Tickets = ticketGroups,
                Orphans = orphans
            };
        }

     
    }
}
