namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Configuration options for ConflictPredictor
/// </summary>
public class ConflictPredictorOptions
{
    /// <summary>
    /// Enable parallel processing for file analysis
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// Maximum degree of parallelism for file analysis
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Maximum number of target branch commits to analyze
    /// </summary>
    public int MaxTargetCommitsToAnalyze { get; set; } = 1000;

    /// <summary>
    /// Enable line-by-line conflict detection (slower but more accurate)
    /// </summary>
    public bool EnableLineConflictDetection { get; set; } = true;

    /// <summary>
    /// Enable semantic conflict detection across related files
    /// </summary>
    public bool EnableSemanticConflictDetection { get; set; } = true;

    /// <summary>
    /// Risk calculation options
    /// </summary>
    public ConflictRiskOptions RiskOptions { get; set; } = new();

    /// <summary>
    /// Show progress display during analysis
    /// </summary>
    public bool ShowProgressDisplay { get; set; } = true;

    /// <summary>
    /// Progress update interval in milliseconds
    /// </summary>
    public int ProgressUpdateIntervalMs { get; set; } = 100;

    /// <summary>
    /// Cache expiration for conflict predictions
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enable detailed logging for debugging
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Patterns for files to skip during analysis
    /// </summary>
    public List<string> SkipFilePatterns { get; set; } =
    [
        "*/bin/*",
        "*/obj/*",
        "*/node_modules/*",
        "*/.git/*",
        "*/packages/*"
    ];

    /// <summary>
    /// File extensions to treat as binary
    /// </summary>
    public HashSet<string> BinaryFileExtensions { get; set; } =
    [
        ".dll", ".exe", ".pdf", ".jpg", ".jpeg", ".png", ".gif",
        ".zip", ".tar", ".gz", ".7z", ".rar",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".mp3", ".mp4", ".avi", ".mov", ".wmv",
        ".jar", ".war", ".ear", ".class"
    ];

    /// <summary>
    /// Patterns for critical files that need special attention
    /// </summary>
    public List<string> CriticalFilePatterns { get; set; } =
    [
        "package.json", "package-lock.json", "yarn.lock",
        "pom.xml", "build.gradle", "build.gradle.kts",
        "*.csproj", "*.sln", "*.vbproj", "*.fsproj",
        "Dockerfile", "docker-compose.yml", "docker-compose.yaml",
        ".gitignore", ".gitmodules", ".gitattributes",
        "appsettings.json", "appsettings.*.json",
        "web.config", "app.config",
        "*.conf", "*.cfg", "*.ini",
        "Makefile", "CMakeLists.txt",
        "requirements.txt", "setup.py", "pyproject.toml"
    ];
}