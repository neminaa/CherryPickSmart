using CherryPickSmart.Models;

namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Represents a predicted conflict between commits
/// </summary>
public record ConflictPrediction
{
    /// <summary>
    /// The file path where conflict is predicted
    /// </summary>
    public string File { get; init; } = "";

    /// <summary>
    /// Related files for semantic conflicts
    /// </summary>
    public List<string> RelatedFiles { get; init; } = [];

    /// <summary>
    /// Commits that will conflict
    /// </summary>
    public List<CpCommit> ConflictingCommits { get; init; } = [];

    /// <summary>
    /// Risk level of the conflict
    /// </summary>
    public ConflictRisk Risk { get; init; }

    /// <summary>
    /// Type of conflict
    /// </summary>
    public ConflictType Type { get; init; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Detailed conflict information
    /// </summary>
    public List<ConflictDetail> Details { get; init; } = [];

    /// <summary>
    /// Suggested resolutions
    /// </summary>
    public List<string> ResolutionSuggestions { get; init; } = [];

    /// <summary>
    /// Modifications in target branch
    /// </summary>
    public List<CommitInfo> TargetModifications { get; init; } = [];

    /// <summary>
    /// Timestamp when prediction was made
    /// </summary>
    public DateTime PredictedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Detailed information about a specific conflict
/// </summary>
public record ConflictDetail
{
    public int LineNumber { get; init; }
    public string ConflictingChange { get; init; } = "";
    public string CommitSha { get; init; } = "";
    public string Author { get; init; } = "";
    public ChangeType ChangeType { get; init; }
}

/// <summary>
/// Information about a commit that modified a file
/// </summary>
public record CommitInfo
{
    public string Sha { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime When { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Risk levels for conflicts
/// </summary>
public enum ConflictRisk
{
    /// <summary>
    /// Low risk - unlikely to cause conflicts
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk - may cause conflicts
    /// </summary>
    Medium,

    /// <summary>
    /// High risk - likely to cause conflicts
    /// </summary>
    High,

    /// <summary>
    /// Certain - will definitely cause conflicts
    /// </summary>
    Certain
}

/// <summary>
/// Types of conflicts
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// Same lines modified by different commits
    /// </summary>
    ContentOverlap,

    /// <summary>
    /// Related code changes that may conflict semantically
    /// </summary>
    SemanticConflict,

    /// <summary>
    /// Binary file conflicts
    /// </summary>
    BinaryFile,

    /// <summary>
    /// File renamed in different ways
    /// </summary>
    FileRenamed,

    /// <summary>
    /// Major structural changes (class/method signatures)
    /// </summary>
    StructuralChange,

    /// <summary>
    /// Import/using statement conflicts
    /// </summary>
    ImportConflict
}

/// <summary>
/// Types of changes in a commit
/// </summary>
public enum ChangeType
{
    Addition,
    Deletion,
    Modification,
    Rename
}