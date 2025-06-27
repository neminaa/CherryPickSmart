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
        public List<CpCommit> PickOrder { get; set; } = [];
    }

    /// <summary>
    /// Builds an AnalysisResult by orchestrating the various engines.
    /// </summary>
    public static class AnalysisResultBuilder
    {
        public static AnalysisResult Build(
            string fromBranch,
            string toBranch,
            IEnumerable<CpCommit> allCommits,
            ICommitParser gitParser,
            IMergeCommitAnalyzer mergeAnalyzer,
            ITicketInferenceEngine ticketEngine,
            IOrphanCommitDetector orphanDetector,
            IOrderOptimizer orderOptimizer)
        {
            // Group commits by ticket (map of ticket -> commits)
            var ticketMap = ticketEngine.GroupByTicket(allCommits);
            // Detect orphans
            var orphans = orphanDetector.Detect(allCommits);

            var ticketGroups = new List<TicketGroup>();
            foreach (var kv in ticketMap)
            {
                var commitsForTicket = kv.Value;
                var group = new TicketGroup
                {
                    Number = kv.Key,
                    MergeCommits = mergeAnalyzer.GetMergeCommits(commitsForTicket),
                    LastCommits = mergeAnalyzer.GetLastCommits(commitsForTicket)
                };

                // Other = those not in Merge or Last
                group.OtherCommits = commitsForTicket
                    .Except(group.MergeCommits)
                    .Except(group.LastCommits)
                    .ToList();

                // PickOrder: merge first, then last, then others
                group.PickOrder = orderOptimizer.ComputePickOrder(group.MergeCommits, group.LastCommits, group.OtherCommits);

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
