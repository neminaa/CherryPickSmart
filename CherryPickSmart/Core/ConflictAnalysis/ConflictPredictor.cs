using CherryPickSmart.Commands;
using CherryPickSmart.Models;
using LibGit2Sharp;
using Spectre.Console;
using System.Text;

namespace CherryPickSmart.Core.ConflictAnalysis;

public class ConflictPredictor
{
    public record ConflictPrediction
    {
        public string File { get; init; } = "";
        public List<CpCommit> ConflictingCommits { get; init; } = [];
        public ConflictRisk Risk { get; init; }
        public ConflictType Type { get; init; }
        public string Description { get; init; } = "";
        public List<ConflictDetail> Details { get; init; } = [];
        public List<string> ResolutionSuggestions { get; init; } = [];
    }

    public record ConflictDetail
    {
        public int LineNumber { get; init; }
        public string ConflictingChange { get; init; } = "";
        public string CommitSha { get; init; } = "";
        public string Author { get; init; } = "";
    }

    public enum ConflictRisk { Low, Medium, High, Certain }

    public enum ConflictType
    {
        ContentOverlap,     // Same lines modified
        SemanticConflict,   // Related code changes
        BinaryFile,         // Binary file conflicts
        FileRenamed,        // File rename conflicts
        StructuralChange,   // Major code structure changes
        ImportConflict      // Import/using statement conflicts
    }

    /// <summary>
    /// Predict conflicts for commits being cherry-picked to target branch
    /// </summary>
    public List<ConflictPrediction> PredictConflicts(
        string repositoryPath,
        List<CpCommit> commitsToCherry,
        string targetBranch)
    {
        using var repo = new Repository(repositoryPath);
        var predictions = new List<ConflictPrediction>();

        // Get target branch state for comparison
        var targetBranchRef = repo.Branches[targetBranch];
        if (targetBranchRef == null)
        {
            throw new ArgumentException($"Target branch '{targetBranch}' not found");
        }

        // Group commits by file for analysis
        var fileCommitMap = BuildFileCommitMap(commitsToCherry);

        // Beautiful progress display with Spectre.Console
        var analysisResults = PerformConflictAnalysisWithProgress(repo, fileCommitMap, targetBranchRef);
        predictions.AddRange(analysisResults);

        // Add cross-file semantic conflict predictions
        var semanticConflicts = AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(Style.Parse("yellow"))
            .Start("🔍 Analyzing semantic conflicts...", ctx =>
            {
                return PredictSemanticConflicts(repo, commitsToCherry, targetBranchRef);
            });

        predictions.AddRange(semanticConflicts);

        // Show final summary
        DisplayFinalSummary(predictions);

        return predictions
            .OrderByDescending(p => p.Risk)
            .ThenByDescending(p => p.ConflictingCommits.Count)
            .ToList();
    }

    /// <summary>
    /// Perform conflict analysis with beautiful progress display
    /// </summary>
    private List<ConflictPrediction> PerformConflictAnalysisWithProgress(
        Repository repo,
        Dictionary<string, List<CpCommit>> fileCommitMap,
        Branch targetBranch)
    {
        var predictions = new List<ConflictPrediction>();
        var totalFiles = fileCommitMap.Count;

        // Create a progress display
        AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(Spinner.Known.Dots2))
            .Start(ctx =>
            {
                // Create progress task
                var mainTask = ctx.AddTask("[green]Analyzing file conflicts[/]", maxValue: totalFiles);

                // Create live display for current analysis
                var analysisDisplay = new LiveDisplayRenderer();

                ctx.Refresh();

                var processedFiles = 0;
                foreach (var (file, commits) in fileCommitMap)
                {
                    // Update main progress
                    mainTask.Description = $"[green]Analyzing[/] [blue]{Path.GetFileName(file)}[/]";

                    // Show current file analysis in a collapsible way
                    analysisDisplay.UpdateCurrentFile(file, commits.Count, processedFiles + 1, totalFiles);

                    try
                    {
                        var prediction = AnalyzeFileConflicts(repo, file, commits, targetBranch);

                        if (prediction is { Risk: > ConflictRisk.Low })
                        {
                            predictions.Add(prediction);
                            analysisDisplay.AddResult(file, prediction.Risk, prediction.Type, true);
                        }
                        else
                        {
                            analysisDisplay.AddResult(file, ConflictRisk.Low, ConflictType.ContentOverlap, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        analysisDisplay.AddError(file, ex.Message);
                    }

                    processedFiles++;
                    mainTask.Value = processedFiles;
                    ctx.Refresh();

                    // Small delay to show progress (remove in production)
                    Thread.Sleep(50);
                }

                mainTask.Description = "[green]✓ File analysis complete[/]";
                mainTask.Value = totalFiles;
            });

        return predictions;
    }

    /// <summary>
    /// Helper class for live display rendering during analysis
    /// </summary>
    private class LiveDisplayRenderer
    {
        private readonly List<(string file, ConflictRisk risk, ConflictType type, bool added)> _results = new();
        private readonly List<(string file, string error)> _errors = new();
        private string _currentFile = "";
        private int _currentCommits = 0;
        private int _currentIndex = 0;
        private int _totalFiles = 0;

        public void UpdateCurrentFile(string file, int commits, int index, int total)
        {
            _currentFile = file;
            _currentCommits = commits;
            _currentIndex = index;
            _totalFiles = total;
        }

        public void AddResult(string file, ConflictRisk risk, ConflictType type, bool added)
        {
            _results.Add((file, risk, type, added));

            // Only show significant results to avoid spam
            if (added && risk >= ConflictRisk.Medium)
            {
                var riskColor = GetRiskColor(risk);
                var typeEmoji = GetTypeEmoji(type);
                AnsiConsole.MarkupLine($"    {typeEmoji} [{riskColor}]{Path.GetFileName(file)}[/] - {risk} risk");
            }
        }

        public void AddError(string file, string error)
        {
            _errors.Add((file, error));
            AnsiConsole.MarkupLine($"    [red]⚠ {Path.GetFileName(file)}[/] - Error: {error.Truncate(50)}");
        }

        private string GetRiskColor(ConflictRisk risk) => risk switch
        {
            ConflictRisk.Certain => "red bold",
            ConflictRisk.High => "red",
            ConflictRisk.Medium => "yellow",
            ConflictRisk.Low => "green",
            _ => "grey"
        };

        private string GetTypeEmoji(ConflictType type) => type switch
        {
            ConflictType.BinaryFile => "📦",
            ConflictType.StructuralChange => "🏗️",
            ConflictType.ImportConflict => "📚",
            ConflictType.SemanticConflict => "🧠",
            ConflictType.FileRenamed => "📝",
            _ => "⚡"
        };
    }

    /// <summary>
    /// Display beautiful final summary
    /// </summary>
    private void DisplayFinalSummary(List<ConflictPrediction> predictions)
    {
        if (!predictions.Any())
        {
            AnsiConsole.MarkupLine("\n[green]✓ No significant conflicts predicted![/]");
            return;
        }

        // Create summary table
        var table = new Table();
        table.AddColumn("[bold]File[/]");
        table.AddColumn("[bold]Risk[/]");
        table.AddColumn("[bold]Type[/]");
        table.AddColumn("[bold]Commits[/]");
        table.AddColumn("[bold]Details[/]");

        // Add high and medium risk predictions to table
        var importantPredictions = predictions
            .Where(p => p.Risk >= ConflictRisk.Medium)
            .Take(10) // Show top 10
            .ToList();

        foreach (var prediction in importantPredictions)
        {
            var riskColor = GetRiskColor(prediction.Risk);
            var typeEmoji = GetTypeEmoji(prediction.Type);

            table.AddRow(
                $"[blue]{Path.GetFileName(prediction.File)}[/]",
                $"[{riskColor}]{prediction.Risk}[/]",
                $"{typeEmoji} {prediction.Type}",
                prediction.ConflictingCommits.Count.ToString(),
                prediction.Details.Count > 0 ? $"{prediction.Details.Count} line conflicts" : "File-level conflict"
            );
        }

        // Create summary panel
        var summaryText = new StringBuilder();
        summaryText.AppendLine($"[green]Total Predictions:[/] {predictions.Count}");
        summaryText.AppendLine($"[red]High Risk:[/] {predictions.Count(p => p.Risk >= ConflictRisk.High)}");
        summaryText.AppendLine($"[yellow]Medium Risk:[/] {predictions.Count(p => p.Risk == ConflictRisk.Medium)}");
        summaryText.AppendLine($"[green]Low Risk:[/] {predictions.Count(p => p.Risk == ConflictRisk.Low)}");

        var summaryPanel = new Panel(summaryText.ToString())
        {
            Header = new PanelHeader("🎯 Conflict Analysis Summary"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("blue")
        };

        // Display results
        AnsiConsole.WriteLine();
        AnsiConsole.Write(summaryPanel);

        if (importantPredictions.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold yellow]⚠️  Significant Conflicts Detected:[/]");
            AnsiConsole.Write(table);
        }

        // Show recommendations for high-risk conflicts
        var highRiskConflicts = predictions.Where(p => p.Risk >= ConflictRisk.High).ToList();
        if (highRiskConflicts.Any())
        {
            AnsiConsole.WriteLine();
            var recommendationsPanel = new Panel(GenerateConflictRecommendations(highRiskConflicts))
            {
                Header = new PanelHeader("💡 Recommendations"),
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("yellow")
            };
            AnsiConsole.Write(recommendationsPanel);
        }
    }

    /// <summary>
    /// Generate recommendations for high-risk conflicts
    /// </summary>
    private string GenerateConflictRecommendations(List<ConflictPrediction> highRiskConflicts)
    {
        var recommendations = new StringBuilder();

        recommendations.AppendLine("[bold red]High-risk conflicts require attention:[/]");
        recommendations.AppendLine();

        foreach (var conflict in highRiskConflicts.Take(5))
        {
            recommendations.AppendLine($"[red]•[/] [blue]{conflict.File}[/]");

            if (conflict.ResolutionSuggestions.Any())
            {
                var suggestion = conflict.ResolutionSuggestions.First();
                recommendations.AppendLine($"  💡 {suggestion}");
            }
            recommendations.AppendLine();
        }

        recommendations.AppendLine("[yellow]Consider:[/]");
        recommendations.AppendLine("• Review conflicts manually before cherry-picking");
        recommendations.AppendLine("• Cherry-pick in smaller batches");
        recommendations.AppendLine("• Coordinate with file authors");

        return recommendations.ToString();
    }

    // Helper methods for styling
    private string GetRiskColor(ConflictRisk risk) => risk switch
    {
        ConflictRisk.Certain => "red bold",
        ConflictRisk.High => "red",
        ConflictRisk.Medium => "yellow",
        ConflictRisk.Low => "green",
        _ => "grey"
    };

    private string GetTypeEmoji(ConflictType type) => type switch
    {
        ConflictType.BinaryFile => "📦",
        ConflictType.StructuralChange => "🏗️",
        ConflictType.ImportConflict => "📚",
        ConflictType.SemanticConflict => "🧠",
        ConflictType.FileRenamed => "📝",
        ConflictType.ContentOverlap => "⚡",
        _ => "❓"
    };

    // [Keep all your existing private methods here - they're good as-is]

    /// <summary>
    /// Build mapping of files to commits that modify them
    /// </summary>
    private Dictionary<string, List<CpCommit>> BuildFileCommitMap(List<CpCommit> commits)
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

        return fileCommitMap;
    }

    /// <summary>
    /// Analyze potential conflicts for a specific file
    /// </summary>
    private ConflictPrediction? AnalyzeFileConflicts(
        Repository repo,
        string filePath,
        List<CpCommit> commits,
        Branch targetBranch)
    {
        try
        {
            // Check if file was modified in target branch recently
            var targetModified = WasFileModifiedInTarget(repo, filePath, targetBranch, commits);

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

            // Analyze line-level conflicts for multiple commits
            var conflictDetails = AnalyzeLineConflicts(repo, filePath, commits);
            var conflictType = DetermineConflictType(filePath, commits, conflictDetails);
            var risk = CalculateConflictRisk(filePath, commits, targetModified, conflictDetails);

            return new ConflictPrediction
            {
                File = filePath,
                ConflictingCommits = commits,
                Risk = risk,
                Type = conflictType,
                Description = GenerateRiskDescription(filePath, commits, risk, targetModified),
                Details = conflictDetails,
                ResolutionSuggestions = GenerateResolutionSuggestions(conflictType, commits, conflictDetails)
            };
        }
        catch (Exception)
        {
            // If we can't analyze the file, assume medium risk
            return new ConflictPrediction
            {
                File = filePath,
                ConflictingCommits = commits,
                Risk = ConflictRisk.Medium,
                Type = ConflictType.ContentOverlap,
                Description = "Unable to analyze file - assume medium risk"
            };
        }
    }

    /// <summary>
    /// Check if file was modified in target branch since the oldest commit being cherry-picked
    /// </summary>
    private bool WasFileModifiedInTarget(Repository repo, string filePath, Branch targetBranch, List<CpCommit> commits)
    {
        try
        {
            var oldestCommitTime = commits.Min(c => c.Timestamp);

            // Get commits in target branch since the oldest commit time
            var filter = new CommitFilter
            {
                IncludeReachableFrom = targetBranch,
                SortBy = CommitSortStrategies.Time
            };

            var recentTargetCommits = repo.Commits.QueryBy(filter)
                .Where(c => c.Author.When.DateTime >= oldestCommitTime)
                .Take(100); // Limit to avoid performance issues

            // Check if any recent target commits modified this file
            foreach (var commit in recentTargetCommits)
            {
                if (!commit.Parents.Any()) continue;

                var parent = commit.Parents.First();
                var changes = repo.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree);

                if (changes.Any(change => change.Path == filePath))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return true; // Assume conflict risk if we can't determine
        }
    }

    /// <summary>
    /// Analyze line-level conflicts between commits
    /// </summary>
    private List<ConflictDetail> AnalyzeLineConflicts(Repository repo, string filePath, List<CpCommit> commits)
    {
        var details = new List<ConflictDetail>();

        try
        {
            var lineModifications = new Dictionary<int, List<(CpCommit commit, string change)>>();

            foreach (var commit in commits)
            {
                var libCommit = repo.Lookup<Commit>(commit.Sha);
                if (libCommit?.Parents?.Any() != true) continue;

                var parent = libCommit.Parents.First();
                var patch = repo.Diff.Compare<Patch>(parent.Tree, libCommit.Tree, [filePath]);

                foreach (var patchEntry in patch)
                {
                    if (patchEntry.Path != filePath) continue;

                    // Parse patch to find modified lines
                    var modifiedLines = ParseModifiedLines(patchEntry.Patch);
                    foreach (var (lineNum, change) in modifiedLines)
                    {
                        if (!lineModifications.ContainsKey(lineNum))
                            lineModifications[lineNum] = [];

                        lineModifications[lineNum].Add((commit, change));
                    }
                }
            }

            // Find lines modified by multiple commits
            foreach (var (lineNum, modifications) in lineModifications.Where(kvp => kvp.Value.Count > 1))
            {
                foreach (var (commit, change) in modifications)
                {
                    details.Add(new ConflictDetail
                    {
                        LineNumber = lineNum,
                        ConflictingChange = change,
                        CommitSha = commit.Sha,
                        Author = commit.Author
                    });
                }
            }
        }
        catch
        {
            // If we can't parse line details, return empty list
        }

        return details;
    }

    /// <summary>
    /// Parse patch text to extract modified line numbers and changes
    /// </summary>
    private List<(int lineNumber, string change)> ParseModifiedLines(string patchText)
    {
        var modifications = new List<(int, string)>();
        var lines = patchText.Split('\n');
        var currentLineNumber = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@"))
            {
                // Parse hunk header: @@ -1,4 +1,6 @@
                var match = System.Text.RegularExpressions.Regex.Match(line, @"@@\s*-\d+,?\d*\s*\+(\d+),?\d*\s*@@");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int startLine))
                {
                    currentLineNumber = startLine;
                }
            }
            else if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                modifications.Add((currentLineNumber, line.Substring(1)));
                currentLineNumber++;
            }
            else if (line.StartsWith(" "))
            {
                currentLineNumber++;
            }
        }

        return modifications;
    }

    /// <summary>
    /// Determine the type of conflict based on file and changes
    /// </summary>
    private ConflictType DetermineConflictType(string filePath, List<CpCommit> commits, List<ConflictDetail> details)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Binary files
        var binaryExtensions = new[] { ".dll", ".exe", ".pdf", ".jpg", ".png", ".gif", ".zip", ".tar", ".gz" };
        if (binaryExtensions.Contains(extension))
        {
            return ConflictType.BinaryFile;
        }

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
                            d.ConflictingChange.Contains("def ")))
        {
            return ConflictType.StructuralChange;
        }

        return ConflictType.ContentOverlap;
    }

    /// <summary>
    /// Calculate conflict risk with improved logic
    /// </summary>
    private ConflictRisk CalculateConflictRisk(
        string filePath,
        List<CpCommit> commits,
        bool targetModified,
        List<ConflictDetail> conflictDetails)
    {
        var riskScore = 0;

        // Base risk for multiple commits on same file
        if (commits.Count > 1)
            riskScore += commits.Count - 1;

        // Time span risk
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);
        if (timeSpan.TotalDays > 7) riskScore += 2;
        if (timeSpan.TotalDays > 30) riskScore += 1;

        // Multiple authors risk
        var authorCount = commits.Select(c => c.Author).Distinct().Count();
        if (authorCount > 1) riskScore += authorCount - 1;

        // Target branch modifications
        if (targetModified) riskScore += 3;

        // Line-level conflicts
        riskScore += conflictDetails.Count / 2;

        // File type risk
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (new[] { ".dll", ".exe", ".pdf" }.Contains(extension))
            riskScore += 2; // Binary files are risky

        return riskScore switch
        {
            >= 8 => ConflictRisk.Certain,
            >= 5 => ConflictRisk.High,
            >= 3 => ConflictRisk.Medium,
            _ => ConflictRisk.Low
        };
    }

    /// <summary>
    /// Predict semantic conflicts across files
    /// </summary>
    private List<ConflictPrediction> PredictSemanticConflicts(Repository repo, List<CpCommit> commits, Branch targetBranch)
    {
        var predictions = new List<ConflictPrediction>();

        // Look for commits that modify related files (same namespace/package)
        var relatedFileGroups = commits
            .SelectMany(c => c.ModifiedFiles.Select(f => new { File = f, Commit = c }))
            .GroupBy(x => GetFileNamespace(x.File))
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in relatedFileGroups)
        {
            var groupCommits = group.Select(x => x.Commit).Distinct().ToList();
            if (groupCommits.Count > 1)
            {
                predictions.Add(new ConflictPrediction
                {
                    File = $"Namespace: {group.Key}",
                    ConflictingCommits = groupCommits,
                    Risk = ConflictRisk.Medium,
                    Type = ConflictType.SemanticConflict,
                    Description = $"Multiple commits modify related files in namespace '{group.Key}'",
                    ResolutionSuggestions =
                    [
                        "Review changes for API compatibility",
                        "Test integration after cherry-pick",
                        "Consider cherry-picking commits in dependency order"
                    ]
                });
            }
        }

        return predictions;
    }

    /// <summary>
    /// Extract namespace/package from file path
    /// </summary>
    private string GetFileNamespace(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? "";
        var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Return the most specific namespace (last 2 directories)
        return string.Join(".", parts.Skip(Math.Max(0, parts.Length - 2)));
    }

    /// <summary>
    /// Generate resolution suggestions based on conflict type
    /// </summary>
    private List<string> GenerateResolutionSuggestions(ConflictType type, List<CpCommit> commits, List<ConflictDetail> details)
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
    private string GenerateRiskDescription(string file, List<CpCommit> commits, ConflictRisk risk, bool targetModified)
    {
        var authors = string.Join(", ", commits.Select(c => c.Author).Distinct());
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);
        var targetNote = targetModified ? " Also modified in target branch." : "";

        return $"File '{file}' modified by {commits.Count} commits (risk: {risk}). " +
               $"Authors: {authors}. Span: {timeSpan.TotalDays:F1} days.{targetNote}";
    }
}