using CherryPickSmart.Commands;
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

        public DateTime AnalysisTimestamp { get; set; }
        public List<TicketAnalysis> TicketAnalyses { get; set; } = [];
        public OrphanAnalysis OrphanAnalysis { get; set; } = null!;
        
        public ConflictAnalysis ConflictAnalysis { get; set; } = null!;
        public AnalysisStatistics Statistics { get; set; } = null!;
        public CherryPickPlan RecommendedPlan { get; set; } = null!;
        public List<ActionableRecommendation> Recommendations { get; set; } = [];
        public AnalysisExport Export { get; set; } = null!;
        public string AnalysisId { get; set; } = Guid.NewGuid().ToString("N");
    }

    public class TicketAnalysis
    {
        public string TicketKey { get; set; } = null!;
        public List<CpCommit> AllCommits { get; set; } = [];
        public List<CpCommit> RegularCommits { get; set; } = [];
        public List<CpCommit> MergeCommits { get; set; } = [];
        public TicketPriority Priority { get; set; }
        public List<string> Authors { get; set; } = [];
        public int TotalFilesModified { get; set; }
        public CherryPickStrategy RecommendedStrategy { get; set; } = null!;
    }
    public enum TicketPriority
    {
        Medium,
        High,
        Low
    }

    public class OrphanAnalysis
    {
        public List<OrphanCommitDetector.OrphanCommit> OrphanCommits { get; set; } = [];
        public OrphanStatistics Statistics { get; set; } = null!;
    }

    public class OrphanStatistics
    {
        public int TotalOrphans { get; set; }
        public int OrphansWithSuggestions { get; set; }
        public int HighPriorityOrphans { get; set; }
    }

    public class ConflictAnalysis
    {
        public List<ConflictPredictor.ConflictPrediction> AllConflicts { get; set; } = [];
        public ConflictStatistics Statistics { get; set; } = null!;
    }

    public class ConflictStatistics
    {
        public int TotalConflicts { get; set; }
        public int HighRiskConflicts { get; set; }
        public int FilesAffected { get; set; }
    }

    public class AnalysisStatistics
    {
        public int TotalCommitsAnalyzed { get; set; }
        public int CommitsWithTickets { get; set; }
        public int OrphanCommits { get; set; }
        public int MergeCommits { get; set; }
        public int TotalTickets { get; set; }
        public int PredictedConflicts { get; set; }
        public double TicketCoverage { get; set; }
        public TimeSpan AnalysisDuration { get; set; }
    }

    public class CherryPickPlan
    {
        public PlanType Type { get; set; }
        public string Summary { get; set; } = null!;
        public RiskAssessment RiskAssessment { get; set; } = null!;
    }

    public class RiskAssessment
    {
        public double OverallRiskScore { get; set; }
        public string RiskLevel { get; set; } = null!;
        public List<string> TopRisks { get; set; } = [];
    }
    public class CherryPickStrategy
    {
        public StrategyType Type { get; set; }
        public string Description { get; set; } = null!;
    }

    public enum PlanType
    {
        Sequential
    }
    public enum StrategyType
    {
        SingleMergeCommit,
        IndividualCommits
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


    public class ActionableRecommendation
    {
        public RecommendationType Type { get; set; }
        public string Title { get; set; } = null!;
        public string Description { get; set; } = null!;
        public RecommendationPriority Priority { get; set; }
    }

    public enum RecommendationPriority
    {
        High,
        Medium,
        Low
    }
    public enum RecommendationType
    {
        ConflictResolution,
        OrphanCommitHandling
    }


    public class AnalysisExport
    {
        public ExportableScript BashScript { get; set; } = null!;
        public ExportableScript PowerShellScript { get; set; } = null!;
        public ExportableReport JsonReport { get; set; } = null!;
        public ExportableReport MarkdownReport { get; set; } = null!;
        public ExportableReport CsvSummary { get; set; } = null!;
    }


}
