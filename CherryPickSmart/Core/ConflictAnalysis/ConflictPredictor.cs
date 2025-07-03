using CherryPickSmart.Models;
using LibGit2Sharp;
using System.Collections.Concurrent;

namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Predicts conflicts for commits being cherry-picked to target branch
/// </summary>
public class ConflictPredictor
{
    protected readonly ConflictPredictorOptions Options;
    protected readonly ConflictRiskCalculator RiskCalculator;

    public ConflictPredictor(ConflictPredictorOptions? options = null)
    {
        Options = options ?? new ConflictPredictorOptions();
        RiskCalculator = new ConflictRiskCalculator(Options.RiskOptions);
    }

    /// <summary>
    /// Predict conflicts for commits being cherry-picked to target branch
    /// </summary>
    public List<ConflictPrediction> PredictConflicts(
        string repositoryPath,
        List<CpCommit> commitsToCherry,
        string targetBranch,
        IConflictAnalysisDisplay? display = null)
    {
        using var repo = new Repository(repositoryPath);
        var predictions = new List<ConflictPrediction>();

        // Get target branch state for comparison
        var targetBranchRef = repo.Branches[targetBranch];
        if (targetBranchRef == null)
        {
            throw new ArgumentException($"Target branch '{targetBranch}' not found");
        }

        // Build caches once for performance
        var oldestCommitTime = commitsToCherry.Min(c => c.Timestamp);
        var targetCache = BuildTargetBranchCache(repo, targetBranchRef, oldestCommitTime);
        var fileCommitMap = BuildFileCommitMap(commitsToCherry);

        // Notify display
        display?.OnAnalysisStarted(fileCommitMap.Count);

        // Analyze each file
        foreach (var (file, commits) in fileCommitMap)
        {
            try
            {
                var pred = AnalyzeFileConflicts(repo, file, commits, targetBranchRef, targetCache);
                if (pred.Risk > ConflictRisk.Low)
                {
                    predictions.Add(pred);
                }
                display?.OnFileAnalyzed(file, pred.Risk > ConflictRisk.Low ? pred : null);
            }
            catch (Exception ex)
            {
                display?.OnError(file, ex.Message);
            }
        }

        // Add semantic conflicts if enabled
        if (Options.EnableSemanticConflictDetection)
        {
            var semanticConflicts = PredictSemanticConflicts(commitsToCherry);
            predictions.AddRange(semanticConflicts);
        }

        // Sort and notify completion
        var sortedPredictions = predictions
            .OrderByDescending(p => p.Risk)
            .ThenByDescending(p => p.ConflictingCommits.Count)
            .ToList();

        display?.OnAnalysisCompleted(sortedPredictions);

        return sortedPredictions;
    }

    /// <summary>
    /// Async version with parallel processing
    /// </summary>
    public async Task<List<ConflictPrediction>> PredictConflictsAsync(
        string repositoryPath,
        List<CpCommit> commitsToCherry,
        string targetBranch,
        IProgress<ConflictAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var repo = new Repository(repositoryPath);
        var predictions = new ConcurrentBag<ConflictPrediction>();

        var targetBranchRef = repo.Branches[targetBranch];
        if (targetBranchRef == null)
            throw new ArgumentException($"Target branch '{targetBranch}' not found");

        // Build caches once
        var oldestCommitTime = commitsToCherry.Min(c => c.Timestamp);
        var targetCache = BuildTargetBranchCache(repo, targetBranchRef, oldestCommitTime);
        var fileCommitMap = BuildFileCommitMap(commitsToCherry);

        var processed = 0;
        var total = fileCommitMap.Count;

        if (Options.EnableParallelProcessing && fileCommitMap.Count > 10)
        {
            await Parallel.ForEachAsync(
                fileCommitMap,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Options.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                }, (kvp, ct) =>
                {
                    var (file, commits) = kvp;

                    // Each thread needs its own repo instance
                    using var threadRepo = new Repository(repositoryPath);
                    var threadTargetBranch = threadRepo.Branches[targetBranch];

                    try
                    {
                        var pred = AnalyzeFileConflicts(threadRepo, file, commits, threadTargetBranch!, targetCache);
                        if (pred.Risk > ConflictRisk.Low)
                            predictions.Add(pred);
                    }
                    catch
                    {
                        // Log error but continue processing
                    }

                    var current = Interlocked.Increment(ref processed);
                    progress?.Report(new ConflictAnalysisProgress
                    {
                        ProcessedFiles = current,
                        TotalFiles = total,
                        CurrentFile = file,
                        FoundConflicts = predictions.Count,
                        PercentComplete = (current * 100.0) / total
                    });
                    return ValueTask.CompletedTask;
                });
        }
        else
        {
            // Sequential processing for small sets
            foreach (var (file, commits) in fileCommitMap)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var pred = AnalyzeFileConflicts(repo, file, commits, targetBranchRef, targetCache);
                    if (pred.Risk > ConflictRisk.Low)
                        predictions.Add(pred);
                }
                catch
                {
                    // Continue processing
                }

                processed++;
                progress?.Report(new ConflictAnalysisProgress
                {
                    ProcessedFiles = processed,
                    TotalFiles = total,
                    CurrentFile = file,
                    FoundConflicts = predictions.Count,
                    PercentComplete = (processed * 100.0) / total
                });
            }
        }

        // Add semantic conflicts
        if (Options.EnableSemanticConflictDetection)
        {
            var semanticConflicts = PredictSemanticConflicts(commitsToCherry);
            foreach (var sc in semanticConflicts)
                predictions.Add(sc);
        }

        return predictions.OrderByDescending(p => p.Risk)
            .ThenByDescending(p => p.ConflictingCommits.Count)
            .ToList();
    }

    /// <summary>
    /// Build cache of target branch modifications
    /// </summary>
    protected TargetBranchCache BuildTargetBranchCache(
        Repository repo,
        Branch targetBranch,
        DateTime oldestCommitTime)
    {
        var cache = new TargetBranchCache { OldestRelevantCommit = oldestCommitTime };

        var filter = new CommitFilter
        {
            IncludeReachableFrom = targetBranch,
            SortBy = CommitSortStrategies.Time
        };

        var recentCommits = repo.Commits.QueryBy(filter)
            .Where(c => c.Author.When.DateTime >= oldestCommitTime)
            .Take(Options.MaxTargetCommitsToAnalyze);

        foreach (var commit in recentCommits)
        {
            if (!commit.Parents.Any()) continue;

            var parent = commit.Parents.First();
            var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);

            foreach (var change in changes)
            {
                cache.ModifiedFiles.Add(change.Path);

                if (!cache.FileModifications.ContainsKey(change.Path))
                    cache.FileModifications[change.Path] = [];

                cache.FileModifications[change.Path].Add(new CommitInfo
                {
                    Sha = commit.Sha,
                    Author = commit.Author.Name,
                    When = commit.Author.When.DateTime,
                    Message = commit.MessageShort
                });
            }
        }

        return cache;
    }

    /// <summary>
    /// Analyze potential conflicts for a specific file
    /// </summary>
    protected virtual ConflictPrediction AnalyzeFileConflicts(
        Repository repo,
        string filePath,
        List<CpCommit> commits,
        Branch targetBranch,
        TargetBranchCache targetCache)
    {
        try
        {
            // Check if file was modified in target branch
            var targetModified = targetCache.ModifiedFiles.Contains(filePath);

            // For single commit, only conflict if target also modified the file
            if (commits.Count == 1 && !targetModified)
            {
                return new ConflictPrediction
                {
                    File = filePath,
                    ConflictingCommits = commits,
                    Risk = ConflictRisk.Low,
                    Type = ConflictType.ContentOverlap,
                    Description = "Single commit, no recent target changes - low conflict risk"
                };
            }

            // Check if this is a binary file
            var isBinary = IsBinaryFile(repo, filePath, commits.First());

            // Analyze line-level conflicts for text files
            var conflictDetails = new List<ConflictDetail>();
            if (!isBinary && Options.EnableLineConflictDetection)
            {
                conflictDetails = AnalyzeLineConflicts(repo, filePath, commits);
            }

            // Determine conflict type and risk
            var conflictType = DetermineConflictType(filePath, conflictDetails, isBinary);

            var riskFactors = new ConflictRiskFactors
            {
                CommitCount = commits.Count,
                AuthorCount = commits.Select(c => c.Author).Distinct().Count(),
                TimeSpanDays = (commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp)).TotalDays,
                LineConflictCount = conflictDetails.Count,
                IsTargetModified = targetModified,
                IsBinaryFile = isBinary,
                IsCriticalFile = IsCriticalFile(filePath)
            };

            var risk = RiskCalculator.CalculateRisk(riskFactors);

            return new ConflictPrediction
            {
                File = filePath,
                ConflictingCommits = commits,
                Risk = risk,
                Type = conflictType,
                Description = GenerateRiskDescription(filePath, commits, risk, targetModified),
                Details = conflictDetails,
                ResolutionSuggestions = GenerateResolutionSuggestions(conflictType),
                TargetModifications = targetCache.FileModifications.GetValueOrDefault(filePath, [])
            };
        }
        catch (Exception ex)
        {
            // If we can't analyze the file, assume medium risk
            return new ConflictPrediction
            {
                File = filePath,
                ConflictingCommits = commits,
                Risk = ConflictRisk.Medium,
                Type = ConflictType.ContentOverlap,
                Description = $"Unable to analyze file: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Build mapping of files to commits that modify them
    /// </summary>
    protected static Dictionary<string, List<CpCommit>> BuildFileCommitMap(List<CpCommit> commits)
    {
        var fileCommitMap = new Dictionary<string, List<CpCommit>>();

        foreach (var commit in commits)
        {
            foreach (var file in commit.ModifiedFiles)
            {
                if (!fileCommitMap.ContainsKey(file))
                    fileCommitMap[file] = [];
                fileCommitMap[file].Add(commit);
            }
        }

        return fileCommitMap.OrderBy(o => o.Key).ToDictionary();
    }

    /// <summary>
    /// Check if file is binary
    /// </summary>
    protected bool IsBinaryFile(Repository repo, string filePath, CpCommit commit)
    {
        try
        {
            var libCommit = repo.Lookup<Commit>(commit.Sha);
            var entry = libCommit?[filePath];
            if (entry?.TargetType == TreeEntryTargetType.Blob)
            {
                var blob = (Blob)entry.Target;
                return blob.IsBinary;
            }
        }
        catch { }

        // Fallback to extension check
        var binaryExtensions = new[] { ".dll", ".exe", ".pdf", ".jpg", ".png", ".gif", ".zip", ".tar", ".gz" };
        return binaryExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
    }

    /// <summary>
    /// Check if file is critical (build files, configs, etc)
    /// </summary>
    protected bool IsCriticalFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        var criticalPatterns = new[]
        {
            "package.json", "pom.xml", ".csproj", ".sln",
            "dockerfile", "docker-compose", ".gitignore",
            "appsettings.json", "web.config", "app.config"
        };

        return criticalPatterns.Any(pattern => fileName.Contains(pattern));
    }

    // Add these methods to ConflictPredictor.cs

    /// <summary>
    /// Analyze line-level conflicts between commits
    /// </summary>
    protected List<ConflictDetail> AnalyzeLineConflicts(Repository repo, string filePath, List<CpCommit> commits)
    {
        var allChanges = new List<LineChange>();
        var conflictDetails = new List<ConflictDetail>();

        foreach (var commit in commits)
        {
            try
            {
                var libCommit = repo.Lookup<Commit>(commit.Sha);
                if (libCommit?.Parents?.Any() != true) continue;

                var parent = libCommit.Parents.First();
                var patch = repo.Diff.Compare<Patch>(parent.Tree, libCommit.Tree, [filePath]);

                foreach (var patchEntry in patch)
                {
                    if (patchEntry.Path != filePath) continue;

                    var changes = ParsePatchWithContext(patchEntry.Patch, commit);
                    allChanges.AddRange(changes);
                }
            }
            catch
            {
                // Continue with other commits
            }
        }

        // Find overlapping changes
        var lineGroups = allChanges
            .GroupBy(c => c.LineRange)
            .Where(g => g.Select(c => c.CommitSha).Distinct().Count() > 1);

        foreach (var group in lineGroups)
        {
            // Check if changes are actually conflicting
            var distinctContents = group.Select(c => c.Content).Distinct().ToList();
            if (distinctContents.Count > 1) // Different changes to same lines
            {
                foreach (var change in group)
                {
                    conflictDetails.Add(new ConflictDetail
                    {
                        LineNumber = change.LineRange.Start,
                        ConflictingChange = change.Content,
                        CommitSha = change.CommitSha,
                        Author = change.Author,
                        ChangeType = change.Type
                    });
                }
            }
        }

        return conflictDetails;
    }

    /// <summary>
    /// Parse patch text to extract changes with context
    /// </summary>
    protected List<LineChange> ParsePatchWithContext(string patchText, CpCommit commit)
    {
        var changes = new List<LineChange>();
        var lines = patchText.Split('\n');
        var currentHunk = new HunkInfo();

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                // Parse hunk header: @@ -1,4 +1,6 @@
                currentHunk = ParseHunkHeader(line);
            }
            else if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                changes.Add(new LineChange
                {
                    LineRange = new LineRange(currentHunk.NewStart, currentHunk.NewStart),
                    Content = line.Substring(1),
                    Type = ChangeType.Addition,
                    CommitSha = commit.Sha,
                    Author = commit.Author
                });
                currentHunk.NewStart++;
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                changes.Add(new LineChange
                {
                    LineRange = new LineRange(currentHunk.OldStart, currentHunk.OldStart),
                    Content = line.Substring(1),
                    Type = ChangeType.Deletion,
                    CommitSha = commit.Sha,
                    Author = commit.Author
                });
                currentHunk.OldStart++;
            }
            else if (line.StartsWith(" "))
            {
                currentHunk.OldStart++;
                currentHunk.NewStart++;
            }
        }

        return changes;
    }

    /// <summary>
    /// Parse hunk header to get line numbers
    /// </summary>
    protected HunkInfo ParseHunkHeader(string header)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            header,
            @"@@\s*-(\d+)(?:,(\d+))?\s*\+(\d+)(?:,(\d+))?\s*@@");

        if (match.Success)
        {
            return new HunkInfo
            {
                OldStart = int.Parse(match.Groups[1].Value),
                OldCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
                NewStart = int.Parse(match.Groups[3].Value),
                NewCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1
            };
        }

        return new HunkInfo();
    }

    /// <summary>
    /// Predict semantic conflicts across files
    /// </summary>
    protected List<ConflictPrediction> PredictSemanticConflicts(List<CpCommit> commits)
    {
        var predictions = new List<ConflictPrediction>();

        // Look for commits that modify related files (same namespace/package)
        var relatedFileGroups = commits
            .SelectMany(c => c.ModifiedFiles.Select(f => new { File = f, Commit = c }))
            .GroupBy(x => GetFileNamespace(x.File))
            .Where(g => g.Select(x => x.Commit).Distinct().Count() > 1)
            .ToList();

        foreach (var group in relatedFileGroups)
        {
            var groupCommits = group.Select(x => x.Commit).Distinct().ToList();
            var files = group.Select(x => x.File).Distinct().ToList();

            predictions.Add(new ConflictPrediction
            {
                File = $"Namespace: {group.Key}",
                RelatedFiles = files,
                ConflictingCommits = groupCommits,
                Risk = ConflictRisk.Medium,
                Type = ConflictType.SemanticConflict,
                Description = $"Multiple commits modify {files.Count} related files in namespace '{group.Key}'",
                ResolutionSuggestions =
                [
                    "Review changes for API compatibility",
                    "Test integration after cherry-pick",
                    "Consider cherry-picking commits in dependency order"
                ]
            });
        }

        return predictions;
    }

    /// <summary>
    /// Extract namespace/package from file path
    /// </summary>
    protected string GetFileNamespace(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? "";
        var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Skip common root directories
        var skipDirs = new[] { "src", "source", "lib", "test", "tests" };
        var relevantParts = parts.Where(p => !skipDirs.Contains(p.ToLowerInvariant())).ToList();

        // Return the most specific namespace (last 2-3 directories)
        var takeCount = Math.Min(3, relevantParts.Count);
        return string.Join(".", relevantParts.Skip(Math.Max(0, relevantParts.Count - takeCount)));
    }

    /// <summary>
    /// Determine the type of conflict based on file and changes
    /// </summary>
    protected ConflictType DetermineConflictType(string filePath, List<ConflictDetail> details, bool isBinary)
    {
        if (isBinary)
        {
            return ConflictType.BinaryFile;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Check for import/using conflicts
        if (details.Any(d => d.ConflictingChange.Contains("using ") ||
                            d.ConflictingChange.Contains("import ") ||
                            d.ConflictingChange.Contains("#include")))
        {
            return ConflictType.ImportConflict;
        }

        // Check for structural changes (class/method definitions)
        if (details.Any(d => d.ConflictingChange.Contains("class ") ||
                            d.ConflictingChange.Contains("interface ") ||
                            d.ConflictingChange.Contains("function ") ||
                            d.ConflictingChange.Contains("def ") ||
                            d.ConflictingChange.Contains("public ") ||
                            d.ConflictingChange.Contains("private ")))
        {
            return ConflictType.StructuralChange;
        }

        // Check for file renames
        if (details.Any(d => d.ChangeType == ChangeType.Rename))
        {
            return ConflictType.FileRenamed;
        }

        return ConflictType.ContentOverlap;
    }

    /// <summary>
    /// Generate resolution suggestions based on conflict type
    /// </summary>
    protected List<string> GenerateResolutionSuggestions(ConflictType type)
    {
        return type switch
        {
            ConflictType.BinaryFile =>
            [
                "Choose the correct binary version manually",
                "Regenerate binary from source if possible",
                "Consider using Git LFS for large binaries"
            ],
            ConflictType.ImportConflict =>
            [
                "Merge import statements carefully",
                "Remove duplicate imports",
                "Check for namespace conflicts"
            ],
            ConflictType.StructuralChange =>
            [
                "Review method signatures for compatibility",
                "Update calling code if interfaces changed",
                "Run full test suite after resolution"
            ],
            ConflictType.SemanticConflict =>
            [
                "Test integration between modified components",
                "Review for breaking API changes",
                "Consider cherry-picking in dependency order"
            ],
            ConflictType.FileRenamed =>
            [
                "Check if file was renamed in both branches",
                "Update import/reference paths",
                "Verify build system recognizes new paths"
            ],
            _ =>
            [
                "Review conflicting lines manually",
                "Keep changes that don't contradict each other",
                "Test the file after resolution"
            ]
        };
    }

    /// <summary>
    /// Generate detailed risk description
    /// </summary>
    protected string GenerateRiskDescription(string file, List<CpCommit> commits, ConflictRisk risk, bool targetModified)
    {
        var authors = commits.Select(c => c.Author).Distinct().ToList();
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);
        var targetNote = targetModified ? " Also modified in target branch." : "";

        var description = $"File modified by {commits.Count} commits";

        if (authors.Count > 1)
            description += $" from {authors.Count} authors";

        if (timeSpan.TotalDays > 1)
            description += $" over {timeSpan.TotalDays:F0} days";

        description += $".{targetNote}";

        return description;
    }

    // Helper classes
    protected class TargetBranchCache
    {
        public HashSet<string> ModifiedFiles { get; set; } = [];
        public Dictionary<string, List<CommitInfo>> FileModifications { get; set; } = new();
        public DateTime OldestRelevantCommit { get; set; }
    }

    protected class LineChange
    {
        public LineRange LineRange { get; set; } = new(0, 0);
        public string Content { get; set; } = "";
        public ChangeType Type { get; set; }
        public string CommitSha { get; set; } = "";
        public string Author { get; set; } = "";
    }

    protected class HunkInfo
    {
        public int OldStart { get; set; }
        public int OldCount { get; set; }
        public int NewStart { get; set; }
        public int NewCount { get; set; }
    }

    protected record LineRange(int Start, int End)
    {
        public bool Overlaps(LineRange other)
        {
            return Start <= other.End && End >= other.Start;
        }
    }

}