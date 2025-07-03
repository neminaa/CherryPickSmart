using CherryPickSmart.Models;
using Spectre.Console;
using System.Text;

namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Extension methods for ConflictPredictor
/// </summary>
public static class ConflictPredictorExtensions
{
    /// <summary>
    /// Display conflict predictions in a beautiful table
    /// </summary>
    public static void DisplayConflictTable(this List<ConflictPrediction> predictions, int maxRows = 10)
    {
        if (predictions.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✓ No conflicts predicted![/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Red)
            .AddColumn(new TableColumn("[bold]File[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Risk[/]").Centered())
            .AddColumn(new TableColumn("[bold]Type[/]").Centered())
            .AddColumn(new TableColumn("[bold]Commits[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Details[/]").LeftAligned());

        var displayPredictions = predictions
            .OrderByDescending(p => p.Risk)
            .ThenByDescending(p => p.ConflictingCommits.Count)
            .Take(maxRows);

        foreach (var prediction in displayPredictions)
        {
            var riskColor = prediction.Risk.GetRiskColor();
            var typeEmoji = prediction.Type.GetTypeEmoji();
            var fileName = Path.GetFileName(prediction.File);
            var directory = Path.GetDirectoryName(prediction.File) ?? "";

            var fileDisplay = string.IsNullOrEmpty(directory)
                ? fileName
                : $"{fileName}\n[dim]{directory}[/]";

            var details = prediction.Details.Any()
                ? $"{prediction.Details.Count} line conflicts"
                : prediction.Description.Length > 50
                    ? prediction.Description.Substring(0, 47) + "..."
                    : prediction.Description;

            table.AddRow(
                fileDisplay,
                $"[{riskColor}]{prediction.Risk}[/]",
                $"{typeEmoji} {prediction.Type}",
                prediction.ConflictingCommits.Count.ToString(),
                $"[dim]{details}[/]"
            );
        }

        AnsiConsole.Write(table);

        if (predictions.Count > maxRows)
        {
            AnsiConsole.MarkupLine($"\n[dim]... and {predictions.Count - maxRows} more conflicts[/]");
        }
    }

    /// <summary>
    /// Generate a detailed conflict report
    /// </summary>
    public static string GenerateDetailedReport(this List<ConflictPrediction> predictions)
    {
        var report = new StringBuilder();

        report.AppendLine("CONFLICT ANALYSIS REPORT");
        report.AppendLine("=======================");
        report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Total Conflicts: {predictions.Count}");
        report.AppendLine();

        // Summary by risk
        var riskGroups = predictions.GroupBy(p => p.Risk).OrderByDescending(g => g.Key);
        report.AppendLine("Summary by Risk Level:");
        foreach (var group in riskGroups)
        {
            report.AppendLine($"  {group.Key}: {group.Count()} conflicts");
        }
        report.AppendLine();

        // Summary by type
        var typeGroups = predictions.GroupBy(p => p.Type).OrderByDescending(g => g.Count());
        report.AppendLine("Summary by Type:");
        foreach (var group in typeGroups)
        {
            report.AppendLine($"  {group.Key}: {group.Count()} conflicts");
        }
        report.AppendLine();

        // Detailed predictions
        report.AppendLine("Detailed Predictions:");
        report.AppendLine("--------------------");

        foreach (var prediction in predictions.OrderByDescending(p => p.Risk))
        {
            report.AppendLine();
            report.AppendLine($"File: {prediction.File}");
            report.AppendLine($"Risk: {prediction.Risk}");
            report.AppendLine($"Type: {prediction.Type}");
            report.AppendLine($"Commits Involved: {string.Join(", ", prediction.ConflictingCommits.Select(c => c.Sha.Substring(0, 8)))}");
            report.AppendLine($"Description: {prediction.Description}");

            if (prediction.Details.Count > 0)
            {
                report.AppendLine("Line Conflicts:");
                foreach (var detail in prediction.Details.Take(5))
                {
                    report.AppendLine($"  Line {detail.LineNumber}: {detail.ConflictingChange}");
                }
                if (prediction.Details.Count > 5)
                {
                    report.AppendLine($"  ... and {prediction.Details.Count - 5} more");
                }
            }

            if (prediction.ResolutionSuggestions.Count > 0)
            {
                report.AppendLine("Suggestions:");
                foreach (var suggestion in prediction.ResolutionSuggestions)
                {
                    report.AppendLine($"  - {suggestion}");
                }
            }
        }

        return report.ToString();
    }

    /// <summary>
    /// Export predictions to JSON
    /// </summary>
    public static string ToJson(this List<ConflictPrediction> predictions, bool indented = true)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        return System.Text.Json.JsonSerializer.Serialize(predictions, options);
    }

    /// <summary>
    /// Get risk color for display
    /// </summary>
    public static string GetRiskColor(this ConflictRisk risk) => risk switch
    {
        ConflictRisk.Certain => "red bold",
        ConflictRisk.High => "red",
        ConflictRisk.Medium => "yellow",
        ConflictRisk.Low => "green",
        _ => "grey"
    };

    /// <summary>
    /// Get type emoji for display
    /// </summary>
    public static string GetTypeEmoji(this ConflictType type) => type switch
    {
        ConflictType.BinaryFile => "📦",
        ConflictType.StructuralChange => "🏗️",
        ConflictType.ImportConflict => "📚",
        ConflictType.SemanticConflict => "🧠",
        ConflictType.FileRenamed => "📝",
        ConflictType.ContentOverlap => "⚡",
        _ => "❓"
    };

    /// <summary>
    /// Filter predictions by minimum risk level
    /// </summary>
    public static List<ConflictPrediction> FilterByMinimumRisk(
        this List<ConflictPrediction> predictions,
        ConflictRisk minimumRisk)
    {
        return predictions.Where(p => p.Risk >= minimumRisk).ToList();
    }

    /// <summary>
    /// Group predictions by directory
    /// </summary>
    public static Dictionary<string, List<ConflictPrediction>> GroupByDirectory(
        this List<ConflictPrediction> predictions)
    {
        return predictions
            .GroupBy(p => Path.GetDirectoryName(p.File) ?? "/")
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Get predictions that involve specific authors
    /// </summary>
    public static List<ConflictPrediction> InvolvingAuthors(
        this List<ConflictPrediction> predictions,
        params string[] authors)
    {
        var authorSet = new HashSet<string>(authors, StringComparer.OrdinalIgnoreCase);
        return predictions
            .Where(p => p.ConflictingCommits.Any(c => authorSet.Contains(c.Author)))
            .ToList();
    }

    /// <summary>
    /// Create a summary panel for display
    /// </summary>
    public static Panel CreateSummaryPanel(this List<ConflictPrediction> predictions)
    {
        var content = new StringBuilder();

        content.AppendLine($"[yellow]Total Conflicts:[/] {predictions.Count}");

        var highRisk = predictions.Count(p => p.Risk >= ConflictRisk.High);
        if (highRisk > 0)
        {
            content.AppendLine($"[red]High Risk:[/] {highRisk}");
        }

        var mediumRisk = predictions.Count(p => p.Risk == ConflictRisk.Medium);
        if (mediumRisk > 0)
        {
            content.AppendLine($"[yellow]Medium Risk:[/] {mediumRisk}");
        }

        var binaryConflicts = predictions.Count(p => p.Type == ConflictType.BinaryFile);
        if (binaryConflicts > 0)
        {
            content.AppendLine($"[blue]Binary Files:[/] {binaryConflicts}");
        }

        return new Panel(content.ToString().TrimEnd())
        {
            Header = new PanelHeader("🎯 Conflict Summary"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("blue")
        };
    }
}

/// <summary>
/// Integration helpers for PlanCommand
/// </summary>
public static class ConflictPredictorIntegration
{
    /// <summary>
    /// Run conflict prediction with Spectre.Console status
    /// </summary>
    public static async Task<List<ConflictPrediction>> PredictConflictsWithStatusAsync(
        string repositoryPath,
        List<CpCommit> commits,
        string targetBranch,
        ConflictPredictorOptions? options = null)
    {
        List<ConflictPrediction> predictions = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("red"))
            .StartAsync("⚠️ Predicting potential conflicts...", async ctx =>
            {
                var progress = new Progress<ConflictAnalysisProgress>(p =>
                {
                    ctx.Status($"⚠️ Analyzing conflicts... {p.PercentComplete:F0}% {p.CurrentFile}");
                });

                var predictor = new ConflictPredictor(options);
                predictions = await predictor.PredictConflictsAsync(
                    repositoryPath,
                    commits,
                    targetBranch,
                    progress);
            });

        return predictions;
    }

    /// <summary>
    /// Run conflict prediction with live display
    /// </summary>
    public static List<ConflictPrediction> PredictConflictsWithLiveDisplay(
        string repositoryPath,
        List<CpCommit> commits,
        string targetBranch,
        ConflictPredictorOptions? options = null)
    {
        List<ConflictPrediction> predictions = null!;
        var display = new SpectreConflictAnalysisDisplay();

        AnsiConsole.Live(display.GetTree())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .Start(ctx =>
            {
                display = new SpectreConflictAnalysisDisplay(ctx);
                var predictor = new ConflictPredictor(options);
                predictions = predictor.PredictConflicts(
                    repositoryPath,
                    commits,
                    targetBranch,
                    display);
            });

        return predictions;
    }
}