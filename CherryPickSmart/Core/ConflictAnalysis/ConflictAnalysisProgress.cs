namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Progress information for conflict analysis
/// </summary>
public class ConflictAnalysisProgress
{
    /// <summary>
    /// Number of files processed
    /// </summary>
    public int ProcessedFiles { get; set; }

    /// <summary>
    /// Total number of files to process
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Current file being analyzed
    /// </summary>
    public string CurrentFile { get; set; } = "";

    /// <summary>
    /// Number of conflicts found so far
    /// </summary>
    public int FoundConflicts { get; set; }

    /// <summary>
    /// Percentage complete (0-100)
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Current phase of analysis
    /// </summary>
    public AnalysisPhase Phase { get; set; }

    /// <summary>
    /// Get a user-friendly status message
    /// </summary>
    public string GetStatusMessage()
    {
        return Phase switch
        {
            AnalysisPhase.BuildingCache => "Building target branch cache...",
            AnalysisPhase.AnalyzingFiles => $"Analyzing {CurrentFile} ({ProcessedFiles}/{TotalFiles})",
            AnalysisPhase.DetectingSemanticConflicts => "Detecting semantic conflicts...",
            AnalysisPhase.Complete => $"Analysis complete. Found {FoundConflicts} conflicts.",
            _ => "Initializing..."
        };
    }
}

/// <summary>
/// Phases of conflict analysis
/// </summary>
public enum AnalysisPhase
{
    Initializing,
    BuildingCache,
    AnalyzingFiles,
    DetectingSemanticConflicts,
    Complete
}

/// <summary>
/// Summary of conflict analysis results
/// </summary>
public class ConflictAnalysisSummary
{
    public int TotalFilesAnalyzed { get; set; }
    public int TotalConflictsFound { get; set; }
    public Dictionary<ConflictRisk, int> ConflictsByRisk { get; set; } = new();
    public Dictionary<ConflictType, int> ConflictsByType { get; set; } = new();
    public List<string> MostConflictedFiles { get; set; } = [];
    public TimeSpan AnalysisDuration { get; set; }
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Get a formatted summary report
    /// </summary>
    public string GetSummaryReport()
    {
        var report = new System.Text.StringBuilder();

        report.AppendLine($"Conflict Analysis Summary");
        report.AppendLine($"========================");
        report.AppendLine($"Files Analyzed: {TotalFilesAnalyzed}");
        report.AppendLine($"Conflicts Found: {TotalConflictsFound}");
        report.AppendLine($"Analysis Time: {AnalysisDuration.TotalSeconds:F2} seconds");
        report.AppendLine();

        if (ConflictsByRisk.Count > 0)
        {
            report.AppendLine("Conflicts by Risk Level:");
            foreach (var (risk, count) in ConflictsByRisk.OrderByDescending(kvp => kvp.Key))
            {
                report.AppendLine($"  {risk}: {count}");
            }
            report.AppendLine();
        }

        if (ConflictsByType.Count > 0)
        {
            report.AppendLine("Conflicts by Type:");
            foreach (var (type, count) in ConflictsByType.OrderByDescending(kvp => kvp.Value))
            {
                report.AppendLine($"  {type}: {count}");
            }
            report.AppendLine();
        }

        if (MostConflictedFiles.Count > 0)
        {
            report.AppendLine("Most Conflicted Files:");
            foreach (var file in MostConflictedFiles.Take(5))
            {
                report.AppendLine($"  - {file}");
            }
        }

        if (Errors.Count > 0)
        {
            report.AppendLine();
            report.AppendLine($"Errors ({Errors.Count}):");
            foreach (var error in Errors.Take(5))
            {
                report.AppendLine($"  - {error}");
            }
        }

        return report.ToString();
    }
}