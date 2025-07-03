namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Calculates conflict risk based on various factors
/// </summary>
public class ConflictRiskCalculator(ConflictRiskOptions? options = null)
{
    private readonly ConflictRiskOptions _options = options ?? new ConflictRiskOptions();

    /// <summary>
    /// Calculate risk level based on various factors
    /// </summary>
    public ConflictRisk CalculateRisk(ConflictRiskFactors factors)
    {
        var score = 0.0;

        // Base factors
        score += CalculateCommitCountScore(factors.CommitCount);
        score += CalculateAuthorCountScore(factors.AuthorCount);
        score += CalculateTimeSpanScore(factors.TimeSpanDays);
        score += CalculateLineConflictScore(factors.LineConflictCount);

        // Apply multipliers
        if (factors.IsTargetModified)
            score *= _options.TargetModifiedMultiplier;

        if (factors.IsBinaryFile)
            score *= _options.BinaryFileMultiplier;

        if (factors.IsCriticalFile)
            score *= _options.CriticalFileMultiplier;

        // Additional factors
        if (factors.HasStructuralChanges)
            score *= _options.StructuralChangeMultiplier;

        if (factors.HasMergeConflictMarkers)
            score += _options.MergeConflictBonus;

        // Normalize to 0-100
        var normalizedScore = Math.Min(100, Math.Max(0, score));

        return DetermineRiskLevel(normalizedScore);
    }

    /// <summary>
    /// Calculate score based on number of commits
    /// </summary>
    private double CalculateCommitCountScore(int commitCount)
    {
        if (commitCount <= 1) return 0;

        // Logarithmic scaling for commit count
        return _options.CommitCountWeight * Math.Log(commitCount + 1);
    }

    /// <summary>
    /// Calculate score based on number of authors
    /// </summary>
    private double CalculateAuthorCountScore(int authorCount)
    {
        if (authorCount <= 1) return 0;

        // More authors = higher risk of conflicting changes
        return _options.AuthorCountWeight * (authorCount - 1);
    }

    /// <summary>
    /// Calculate score based on time span
    /// </summary>
    private double CalculateTimeSpanScore(double timeSpanDays)
    {
        if (timeSpanDays <= 1) return 0;

        // Longer time spans increase risk
        if (timeSpanDays > 30)
            return _options.TimeSpanWeight * 10;
        else if (timeSpanDays > 14)
            return _options.TimeSpanWeight * 7;
        else if (timeSpanDays > 7)
            return _options.TimeSpanWeight * 5;
        else
            return _options.TimeSpanWeight * timeSpanDays;
    }

    /// <summary>
    /// Calculate score based on line conflicts
    /// </summary>
    private double CalculateLineConflictScore(int lineConflictCount)
    {
        if (lineConflictCount == 0) return 0;

        // Square root scaling to prevent overwhelming the score
        return _options.LineConflictWeight * Math.Sqrt(lineConflictCount);
    }

    /// <summary>
    /// Determine risk level from normalized score
    /// </summary>
    private ConflictRisk DetermineRiskLevel(double normalizedScore)
    {
        if (normalizedScore >= _options.CertainThreshold)
            return ConflictRisk.Certain;
        if (normalizedScore >= _options.HighThreshold)
            return ConflictRisk.High;
        return normalizedScore >= _options.MediumThreshold ? ConflictRisk.Medium : ConflictRisk.Low;
    }

    /// <summary>
    /// Get human-readable explanation of risk calculation
    /// </summary>
    public string ExplainRisk(ConflictRiskFactors factors, ConflictRisk calculatedRisk)
    {
        var explanations = new List<string>();

        if (factors.CommitCount > 3)
            explanations.Add($"{factors.CommitCount} commits modify this file");

        if (factors.AuthorCount > 2)
            explanations.Add($"{factors.AuthorCount} different authors");

        if (factors.TimeSpanDays > 7)
            explanations.Add($"Changes span {factors.TimeSpanDays:F0} days");

        if (factors.LineConflictCount > 0)
            explanations.Add($"{factors.LineConflictCount} conflicting lines");

        if (factors.IsTargetModified)
            explanations.Add("File also modified in target branch");

        if (factors.IsBinaryFile)
            explanations.Add("Binary file (manual resolution required)");

        if (factors.IsCriticalFile)
            explanations.Add("Critical configuration/build file");

        var riskLevel = calculatedRisk.ToString().ToUpper();
        return $"{riskLevel} RISK: {string.Join(", ", explanations)}";
    }
}

/// <summary>
/// Factors used to calculate conflict risk
/// </summary>
public class ConflictRiskFactors
{
    public int CommitCount { get; set; }
    public int AuthorCount { get; set; }
    public double TimeSpanDays { get; set; }
    public int LineConflictCount { get; set; }
    public bool IsTargetModified { get; set; }
    public bool IsBinaryFile { get; set; }
    public bool IsCriticalFile { get; set; }
    public bool HasStructuralChanges { get; set; }
    public bool HasMergeConflictMarkers { get; set; }
}

/// <summary>
/// Options for risk calculation
/// </summary>
public class ConflictRiskOptions
{
    // Base weights
    public double CommitCountWeight { get; set; } = 5.0;
    public double AuthorCountWeight { get; set; } = 3.0;
    public double TimeSpanWeight { get; set; } = 0.5;
    public double LineConflictWeight { get; set; } = 2.0;

    // Multipliers
    public double TargetModifiedMultiplier { get; set; } = 2.0;
    public double BinaryFileMultiplier { get; set; } = 1.5;
    public double CriticalFileMultiplier { get; set; } = 1.3;
    public double StructuralChangeMultiplier { get; set; } = 1.2;

    // Bonuses
    public double MergeConflictBonus { get; set; } = 20.0;

    // Thresholds (0-100 scale)
    public double CertainThreshold { get; set; } = 80;
    public double HighThreshold { get; set; } = 60;
    public double MediumThreshold { get; set; } = 30;

    /// <summary>
    /// Create options optimized for conservative risk assessment
    /// </summary>
    public static ConflictRiskOptions Conservative => new()
    {
        CommitCountWeight = 7.0,
        AuthorCountWeight = 4.0,
        TargetModifiedMultiplier = 2.5,
        CertainThreshold = 70,
        HighThreshold = 50,
        MediumThreshold = 25
    };

    /// <summary>
    /// Create options optimized for aggressive risk assessment
    /// </summary>
    public static ConflictRiskOptions Aggressive => new()
    {
        CommitCountWeight = 3.0,
        AuthorCountWeight = 2.0,
        TargetModifiedMultiplier = 1.5,
        CertainThreshold = 90,
        HighThreshold = 70,
        MediumThreshold = 40
    };
}