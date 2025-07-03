using CherryPickSmart.Core.ConflictAnalysis;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.TicketAnalysis;
using CherryPickSmart.Models;
using CherryPickSmart.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace CherryPickSmart.Commands;

[Verb("analyze", HelpText = "Analyze commits between branches")]
public class AnalyzeCommand : ICommand
{
    [Option('f', "from", Required = true, HelpText = "Source branch (e.g., deploy/dev)")]
    public string FromBranch { get; set; } = "";

    [Option('t', "to", Required = true, HelpText = "Target branch (e.g., deploy/uat)")]
    public string ToBranch { get; set; } = "";

    [Option('o', "orphans", HelpText = "Show detailed orphan analysis")]
    public bool ShowOrphans { get; set; }

    [Option('r', "repo", Required = true, HelpText = "Path to the repository")]
    public string RepositoryPath { get; set; } = "";

    [Option("export-scripts", HelpText = "Generate cherry-pick scripts")]
    public bool ExportScripts { get; set; } = true;

    [Option("format", Default = "console", HelpText = "Output format: console, json, html, all")]
    public string OutputFormat { get; set; } = "console";

    [Option("output-dir", HelpText = "Directory for output files")]
    public string? OutputDirectory { get; set; }

    public async Task<int> ExecuteAsync(IServiceProvider services)
    {
        try
        {
            // Welcome header
            var rule = new Rule("[bold cyan]CherryPick Smart Analysis[/]")
            {
                Style = Style.Parse("cyan")
            };
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Parameter[/]").Centered())
                .AddColumn(new TableColumn("[bold]Value[/]").LeftAligned());

            table.AddRow("[cyan]Source Branch[/]", $"[yellow]{FromBranch}[/]");
            table.AddRow("[cyan]Target Branch[/]", $"[yellow]{ToBranch}[/]");
            table.AddRow("[cyan]Repository[/]", $"[dim]{RepositoryPath}[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            AnalysisResult analysisResult;

            //AnsiConsole.Status()
            //    .Spinner(Spinner.Known.Dots)
            //    .SpinnerStyle(Style.Parse("green"))
            //    .Start("Analyzing commits...", ctx =>
            //    {

            //    });

            // Step 1: Parse Git History
            AnsiConsole.WriteLine("📊 Parsing Git history...");
            var parser = services.GetRequiredService<GitHistoryParser>();
            var graph = parser.ParseHistory(RepositoryPath, FromBranch, ToBranch);
            var targetCommits = parser.GetCommitsInBranch(RepositoryPath, ToBranch);

            // Step 2: Extract Tickets
            AnsiConsole.WriteLine("🎫 Extracting tickets from commit messages...");
            var ticketExtractor = services.GetRequiredService<TicketExtractor>();
            var ticketMap = ticketExtractor.BuildTicketCommitMap(graph);

            // Step 3: Analyze Merge Commits
            AnsiConsole.WriteLine("🔀 Analyzing merge commits...");
            var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
            var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);

            // Step 4: Detect Orphan Commits
            AnsiConsole.WriteLine("🏴 Detecting orphaned commits...");
            var orphanDetector = services.GetRequiredService<OrphanCommitDetector>();
            var orphanCommits = orphanDetector.FindOrphans(graph, ticketMap);

            // Step 5: Predict Conflicts
            AnsiConsole.WriteLine("⚠️  Predicting potential conflicts...");
            var conflictPredictor = services.GetRequiredService<ConflictPredictor>();
            var commits = graph.Commits.Values.Select(c => c).ToList();
            var conflictPredictions = conflictPredictor.PredictConflicts(
                RepositoryPath, commits, ToBranch);

            // Step 6: Build Comprehensive Analysis Result
            AnsiConsole.WriteLine("📋 Building comprehensive analysis...");
            analysisResult = BuildAnalysisFromCalculatedData(
                FromBranch,
                ToBranch,
                graph,
                ticketMap,
                orphanCommits,
                conflictPredictions);
            // Step 7: Display Results
            await DisplayResults(analysisResult);

            // Step 8: Export Results
            if (ShouldExportResults())
            {
                await ExportResults(analysisResult);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold green]✅ Analysis completed successfully![/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]❌ Error during analysis[/]");

            var panel = new Panel(new Markup($"[red]{ex.Message.EscapeMarkup()}[/]"))
                .Header("[red]Error Details[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Red);

            AnsiConsole.Write(panel);

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Stack trace:[/]");
                AnsiConsole.WriteLine(ex.StackTrace);
            }

            return 1;
        }
    }

    /// <summary>
    /// Build analysis result using pre-calculated data instead of recalculating
    /// </summary>
    private AnalysisResult BuildAnalysisFromCalculatedData(
        string fromBranch,
        string toBranch,
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketMap,
        List<OrphanCommitDetector.OrphanCommit> orphanCommits,
        List<ConflictPrediction> conflictPredictions)
    {
        var analysisStart = DateTime.UtcNow;

        var result = new AnalysisResult
        {
            FromBranch = fromBranch,
            ToBranch = toBranch,
            AnalysisTimestamp = analysisStart,
            TicketAnalyses = BuildTicketAnalyses(ticketMap),
            OrphanAnalysis = BuildOrphanAnalysis(orphanCommits),
            ConflictAnalysis = BuildConflictAnalysis(conflictPredictions),
            Statistics = BuildStatistics(graph, ticketMap, orphanCommits, conflictPredictions, analysisStart),
            RecommendedPlan = BuildCherryPickPlan(ticketMap, conflictPredictions),
            Recommendations = BuildActionableRecommendations(orphanCommits, conflictPredictions),
            Export = BuildExportOptions(fromBranch, toBranch)
        };

        return result;
    }

    /// <summary>
    /// Display analysis results to console
    /// </summary>
    private async Task DisplayResults(AnalysisResult result)
    {
        AnsiConsole.WriteLine();

        // Analysis Report Header
        var headerPanel = new Panel(new Markup($"[bold white]CHERRY-PICK ANALYSIS REPORT[/]\n" +
                                              $"[cyan]From:[/] [yellow]{result.FromBranch}[/] [cyan]→[/] [yellow]{result.ToBranch}[/]\n" +
                                              $"[dim]Analysis ID: {result.AnalysisId}[/]\n" +
                                              $"[dim]Generated: {result.AnalysisTimestamp:yyyy-MM-dd HH:mm:ss}[/]"))
            .Header("[bold blue]📊 Analysis Report[/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Blue);

        AnsiConsole.Write(headerPanel);
        AnsiConsole.WriteLine();

        // Display Summary Statistics
        DisplaySummaryStatistics(result.Statistics);

        // Display Ticket Analysis
        if (result.TicketAnalyses.Count > 0)
        {
            DisplayTicketAnalysis(result);
        }

        // Display Top Recommendations
        if (result.Recommendations.Count > 0)
        {
            DisplayRecommendations(result.Recommendations);
        }

        // Display Orphan Summary if requested
        if (ShowOrphans && result.OrphanAnalysis.OrphanCommits.Count > 0)
        {
            DisplayOrphanAnalysis(result.OrphanAnalysis);
        }

        // Display Risk Assessment
        DisplayRiskAssessment(result.RecommendedPlan);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Display summary statistics
    /// </summary>
    private static void DisplaySummaryStatistics(AnalysisStatistics stats)
    {
        var statsTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Details[/]").LeftAligned());

        statsTable.AddRow(
            "[cyan]Total Commits[/]",
            $"[bold white]{stats.TotalCommitsAnalyzed:N0}[/]",
            "📝 All commits analyzed");

        statsTable.AddRow(
            "[cyan]Commits with Tickets[/]",
            $"[bold green]{stats.CommitsWithTickets:N0}[/]",
            $"📊 {stats.TicketCoverage:P1} coverage");

        var orphanColor = stats.OrphanCommits > 0 ? "yellow" : "green";
        statsTable.AddRow(
            "[cyan]Orphan Commits[/]",
            $"[bold {orphanColor}]{stats.OrphanCommits:N0}[/]",
            "🏴 No ticket association");

        statsTable.AddRow(
            "[cyan]Merge Commits[/]",
            $"[bold blue]{stats.MergeCommits:N0}[/]",
            "🔀 Merge operations");

        statsTable.AddRow(
            "[cyan]Total Tickets[/]",
            $"[bold white]{stats.TotalTickets:N0}[/]",
            "🎫 Unique tickets found");

        var conflictColor = stats.PredictedConflicts > 0 ? "red" : "green";
        statsTable.AddRow(
            "[cyan]Predicted Conflicts[/]",
            $"[bold {conflictColor}]{stats.PredictedConflicts:N0}[/]",
            "⚠️  Potential issues");

        statsTable.AddRow(
            "[cyan]Analysis Duration[/]",
            $"[bold dim]{stats.AnalysisDuration.TotalSeconds:F1}s[/]",
            "⏱️  Processing time");

        var panel = new Panel(statsTable)
            .Header("[bold green]📈 Summary Statistics[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Green);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display ticket analysis in a formatted table
    /// </summary>
    private static void DisplayTicketAnalysis(AnalysisResult result)
    {
        var ticketTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn(new TableColumn("[bold]Ticket[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Commits[/]").Centered())
            .AddColumn(new TableColumn("[bold]Priority[/]").Centered())
            .AddColumn(new TableColumn("[bold]Strategy[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Files[/]").Centered())
            .AddColumn(new TableColumn("[bold]Authors[/]").Centered())
            .AddColumn(new TableColumn("[bold]Conflicts[/]").Centered());

        var displayTickets = result.TicketAnalyses.Take(10);

        foreach (var ticket in displayTickets)
        {
            var conflicts = result.ConflictAnalysis
                .AllConflicts
                .SelectMany(s => s.ConflictingCommits)
                .DistinctBy(r => r.Sha)
                .Where(w => w.ExtractedTickets.Contains(ticket.TicketKey))
                .ToList();

            var priorityColor = ticket.Priority switch
            {
                TicketPriority.High => "red",
                TicketPriority.Medium => "yellow",
                TicketPriority.Low => "green",
                _ => "dim"
            };

            var conflictDisplay = conflicts.Count > 0
                ? $"[red]⚠️  {conflicts.Count}[/]"
                : "[green]✅ 0[/]";

            ticketTable.AddRow(
                $"[bold cyan]{ticket.TicketKey}[/]",
                $"[white]{ticket.AllCommits.Count}[/]",
                $"[{priorityColor}]{ticket.Priority}[/]",
                $"[dim]{ticket.RecommendedStrategy.Type}[/]",
                $"[blue]{ticket.TotalFilesModified}[/]",
                $"[green]{ticket.Authors.Count}[/]",
                conflictDisplay);
        }

        var panel = new Panel(ticketTable)
            .Header($"[bold yellow]🎫 Ticket Analysis[/] [dim]({result.TicketAnalyses.Count} total)[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow);

        AnsiConsole.Write(panel);

        if (result.TicketAnalyses.Count > 10)
        {
            AnsiConsole.MarkupLine($"[dim]... and {result.TicketAnalyses.Count - 10} more tickets[/]");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display recommendations with nice formatting
    /// </summary>
    private static void DisplayRecommendations(List<ActionableRecommendation> recommendations)
    {
        var recTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold]Priority[/]").Centered())
            .AddColumn(new TableColumn("[bold]Recommendation[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned());

        foreach (var rec in recommendations.Take(5))
        {
            var priorityColor = rec.Priority switch
            {
                RecommendationPriority.High => "red",
                RecommendationPriority.Medium => "yellow",
                RecommendationPriority.Low => "green",
                _ => "dim"
            };

            var priorityIcon = rec.Priority switch
            {
                RecommendationPriority.High => "🔥",
                RecommendationPriority.Medium => "⚡",
                RecommendationPriority.Low => "💡",
                _ => "ℹ️"
            };

            recTable.AddRow(
                $"[{priorityColor}]{priorityIcon} {rec.Priority}[/]",
                $"[bold white]{rec.Title}[/]",
                $"[dim]{rec.Description}[/]");
        }

        var panel = new Panel(recTable)
            .Header("[bold blue]💡 Top Recommendations[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display orphan analysis
    /// </summary>
    private static void DisplayOrphanAnalysis(OrphanAnalysis orphanAnalysis)
    {
        var orphanTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Orange1)
            .AddColumn(new TableColumn("[bold]Commit[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Message[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Reason[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Severity[/]").Centered())
            .AddColumn(new TableColumn("[bold]Suggestion[/]").LeftAligned());

        foreach (var orphan in orphanAnalysis.OrphanCommits.Take(10))
        {
            var severityColor = orphan.Severity switch
            {
                OrphanCommitDetector.OrphanSeverity.High => "red",
                OrphanCommitDetector.OrphanSeverity.Medium => "yellow",
                OrphanCommitDetector.OrphanSeverity.Low => "green",
                _ => "dim"
            };

            var suggestion = orphan.Suggestions.Count >0
                ? orphan.Suggestions.OrderByDescending(s => s.Confidence).First()
                : null;

            var suggestionText = suggestion != null
                ? $"[cyan]{suggestion.TicketKey}[/] [dim]({suggestion.Confidence:F1}%)[/]"
                : "[dim]None[/]";

            orphanTable.AddRow(
                $"[bold yellow]{orphan.Commit.ShortSha}[/]",
                $"[white]{orphan.Commit.Message.Truncate(40)}[/]",
                $"[dim]{orphan.Reason}[/]",
                $"[{severityColor}]{orphan.Severity}[/]",
                suggestionText);
        }

        var panel = new Panel(orphanTable)
            .Header($"[bold orange1]🏴 Orphaned Commits[/] [dim]({orphanAnalysis.OrphanCommits.Count} total)[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Orange1);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display risk assessment
    /// </summary>
    private void DisplayRiskAssessment(CherryPickPlan plan)
    {
        var riskColor = plan.RiskAssessment.RiskLevel.ToLower() switch
        {
            "critical" => "red",
            "high" => "red",
            "medium" => "yellow",
            "low" => "green",
            _ => "dim"
        };

        var riskIcon = plan.RiskAssessment.RiskLevel.ToLower() switch
        {
            "critical" => "🚨",
            "high" => "⚠️",
            "medium" => "⚡",
            "low" => "✅",
            _ => "ℹ️"
        };

        // Create a progress bar for risk score
        //AnsiConsole.Progress()
        //    .Start(context => {})
        //var riskProgress = new ProgressBar()
        //    .Value(plan.RiskAssessment.OverallRiskScore)
        //    .MaxValue(100);
            
        var riskContent = new Rows(
            new Markup($"[bold]Overall Risk Level:[/] [{riskColor}]{riskIcon} {plan.RiskAssessment.RiskLevel}[/]"),
            new Markup($"[bold]Risk Score:[/] [{riskColor}]{plan.RiskAssessment.OverallRiskScore:F1}/100[/]"),
            //riskProgress,
            new Markup(""),
            new Markup("[bold]Top Risk Factors:[/]")
        );

        if (plan.RiskAssessment.TopRisks.Count > 0)
        {
            foreach (var risk in plan.RiskAssessment.TopRisks)
            {
                riskContent = new Rows(riskContent, new Markup($"[red]• {risk}[/]"));
            }
        }
        else
        {
            riskContent = new Rows(riskContent, new Markup("[green]• No major risks identified[/]"));
        }

        var panel = new Panel(riskContent)
            .Header("[bold red]🎯 Risk Assessment[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Red);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Determine if we should export results based on options
    /// </summary>
    private bool ShouldExportResults()
    {
        return ExportScripts ||
               OutputFormat != "console" ||
               !string.IsNullOrEmpty(OutputDirectory);
    }

    /// <summary>
    /// Export results in requested formats
    /// </summary>
    private async Task ExportResults(AnalysisResult result)
    {
        var outputDir = OutputDirectory ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDir);

        AnsiConsole.WriteLine();

        var exportPanel = new Panel(new Markup($"[dim]Output Directory: {outputDir}[/]"))
            .Header("[bold cyan]📤 Exporting Results[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1);

        AnsiConsole.Write(exportPanel);
        AnsiConsole.WriteLine();

        try
        {
            await AnsiConsole.Progress()
                .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    // Export based on format option
                    if (OutputFormat is "json" or "all")
                    {
                        var jsonTask = ctx.AddTask("[green]Exporting JSON report[/]");
                        await ExportJsonReport(result, outputDir);
                        jsonTask.Value = 100;
                    }

                    if (OutputFormat is "html" or "all")
                    {
                        var htmlTask = ctx.AddTask("[blue]Exporting HTML report[/]");
                        await ExportHtmlReport(result, outputDir);
                        htmlTask.Value = 100;
                    }

                    if (ExportScripts || OutputFormat == "all")
                    {
                        var scriptTask = ctx.AddTask("[yellow]Exporting scripts[/]");
                        await ExportScriptsAsync(result, outputDir);
                        scriptTask.Value = 100;
                    }

                    if (OutputFormat == "all")
                    {
                        var csvTask = ctx.AddTask("[cyan]Exporting CSV summary[/]");
                        await ExportCsvSummary(result, outputDir);
                        csvTask.Value = 100;

                        var mdTask = ctx.AddTask("[magenta]Exporting Markdown report[/]");
                        await ExportMarkdownReport(result, outputDir);
                        mdTask.Value = 100;
                    }
                });

            AnsiConsole.MarkupLine("[bold green]✅ All exports completed successfully![/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]⚠️  Error during export: {ex.Message.EscapeMarkup()}[/]");
        }
    }

    private async Task ExportJsonReport(AnalysisResult result, string outputDir)
    {
        var fileName = result.Export.JsonReport.FileName;
        var filePath = Path.Combine(outputDir, fileName);

        var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(filePath, json);
        AnsiConsole.MarkupLine($"  [green]✅ JSON report:[/] [link]{filePath}[/]");
    }

    private static async Task ExportHtmlReport(AnalysisResult result, string outputDir)
    {
        var fileName = result.Export.MarkdownReport.FileName.Replace(".md", ".html");
        var filePath = Path.Combine(outputDir, fileName);

        var html = GenerateHtmlReport(result);
        await File.WriteAllTextAsync(filePath, html);
        AnsiConsole.MarkupLine($"  [blue]✅ HTML report:[/] [link]{filePath}[/]");
    }

    private async Task ExportScriptsAsync(AnalysisResult result, string outputDir)
    {
        // Export bash script
        var bashPath = Path.Combine(outputDir, result.Export.BashScript.FileName);
        var bashContent = GenerateBashScript(result);
        await File.WriteAllTextAsync(bashPath, bashContent);
        AnsiConsole.MarkupLine($"  [yellow]✅ Bash script:[/] [link]{bashPath}[/]");

        // Export PowerShell script  
        var psPath = Path.Combine(outputDir, result.Export.PowerShellScript.FileName);
        var psContent = GeneratePowerShellScript(result);
        await File.WriteAllTextAsync(psPath, psContent);
        AnsiConsole.MarkupLine($"  [blue]✅ PowerShell script:[/] [link]{psPath}[/]");
    }

    private async Task ExportCsvSummary(AnalysisResult result, string outputDir)
    {
        var fileName = result.Export.CsvSummary.FileName;
        var filePath = Path.Combine(outputDir, fileName);

        var csv = GenerateCsvSummary(result);
        await File.WriteAllTextAsync(filePath, csv);
        AnsiConsole.MarkupLine($"  [cyan]✅ CSV summary:[/] [link]{filePath}[/]");
    }

    private async Task ExportMarkdownReport(AnalysisResult result, string outputDir)
    {
        var fileName = result.Export.MarkdownReport.FileName;
        var filePath = Path.Combine(outputDir, fileName);

        var markdown = GenerateMarkdownReport(result);
        await File.WriteAllTextAsync(filePath, markdown);
        AnsiConsole.MarkupLine($"  [magenta]✅ Markdown report:[/] [link]{filePath}[/]");
    }

    // Helper methods for building analysis components (simplified versions)
    // In practice, these would use the comprehensive builder methods we designed

    private static List<TicketAnalysis> BuildTicketAnalyses(
        Dictionary<string, List<CpCommit>> ticketMap)
    {
        return ticketMap.Select(kvp => new TicketAnalysis
        {
            TicketKey = kvp.Key,
            AllCommits = kvp.Value,
            RegularCommits = kvp.Value.Where(c => !c.IsMergeCommit).ToList(),
            MergeCommits = kvp.Value.Where(c => c.IsMergeCommit).ToList(),
            Priority = DetermineTicketPriority(kvp.Key, kvp.Value),
            Authors = kvp.Value.Select(c => c.Author).Distinct().ToList(),
            TotalFilesModified = kvp.Value.SelectMany(c => c.ModifiedFiles).Distinct().Count(),
            RecommendedStrategy = new CherryPickStrategy
            {
                Type = kvp.Value.Any(c => c.IsMergeCommit) ? StrategyType.SingleMergeCommit : StrategyType.IndividualCommits,
                Description = "Standard cherry-pick strategy"
            }
        }).ToList();
    }

    private OrphanAnalysis BuildOrphanAnalysis(List<OrphanCommitDetector.OrphanCommit> orphanCommits)
    {
        return new OrphanAnalysis
        {
            OrphanCommits = orphanCommits,
            Statistics = new OrphanStatistics
            {
                TotalOrphans = orphanCommits.Count,
                OrphansWithSuggestions = orphanCommits.Count(o => o.Suggestions.Count > 0),
                HighPriorityOrphans = orphanCommits.Count(o => o.Severity >= OrphanCommitDetector.OrphanSeverity.High)
            }
        };
    }

    private static ConflictAnalysis BuildConflictAnalysis(List<ConflictPrediction> conflictPredictions)
    {
        return new ConflictAnalysis
        {
            AllConflicts = conflictPredictions,
            Statistics = new ConflictStatistics
            {
                TotalConflicts = conflictPredictions.Count,
                HighRiskConflicts = conflictPredictions.Count(c => c.Risk >= ConflictRisk.High),
                FilesAffected = conflictPredictions.Select(c => c.File).Distinct().Count()
            }
        };
    }

    private AnalysisStatistics BuildStatistics(
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketMap,
        List<OrphanCommitDetector.OrphanCommit> orphanCommits,
        List<ConflictPrediction> conflictPredictions,
        DateTime analysisStart)
    {
        var totalCommits = graph.Commits.Count;
        var commitsWithTickets = ticketMap.Values.SelectMany(c => c).Select(c => c.Sha).Distinct().Count();

        return new AnalysisStatistics
        {
            TotalCommitsAnalyzed = totalCommits,
            CommitsWithTickets = commitsWithTickets,
            OrphanCommits = orphanCommits.Count,
            MergeCommits = graph.Commits.Values.Count(c => c.IsMergeCommit),
            TotalTickets = ticketMap.Count,
            PredictedConflicts = conflictPredictions.Count,
            TicketCoverage = totalCommits > 0 ? (double)commitsWithTickets / totalCommits : 0,
            AnalysisDuration = DateTime.UtcNow - analysisStart
        };
    }

    private CherryPickPlan BuildCherryPickPlan(
        Dictionary<string, List<CpCommit>> ticketMap,
        List<ConflictPrediction> conflictPredictions)
    {
        var riskScore = conflictPredictions.Count(c => c.Risk >= ConflictRisk.High) * 20.0;

        return new CherryPickPlan
        {
            Type = PlanType.Sequential,
            Summary = $"Cherry-pick {ticketMap.Count} tickets with {conflictPredictions.Count} potential conflicts",
            RiskAssessment = new RiskAssessment
            {
                OverallRiskScore = Math.Min(100, riskScore),
                RiskLevel = riskScore switch
                {
                    >= 80 => "Critical",
                    >= 60 => "High",
                    >= 40 => "Medium",
                    _ => "Low"
                },
                TopRisks = conflictPredictions.Where(c => c.Risk >= ConflictRisk.High)
                    .Select(c => $"Conflict in {c.File}")
                    .Take(5).ToList()
            }
        };
    }

    private List<ActionableRecommendation> BuildActionableRecommendations(List<OrphanCommitDetector.OrphanCommit> orphanCommits,
        List<ConflictPrediction> conflictPredictions)
    {
        var recommendations = new List<ActionableRecommendation>();

        if (conflictPredictions.Any(c => c.Risk >= ConflictRisk.High))
        {
            recommendations.Add(new ActionableRecommendation
            {
                Type = RecommendationType.ConflictResolution,
                Title = "Review High-Risk Conflicts",
                Description = "Several files have high conflict risk",
                Priority = RecommendationPriority.High
            });
        }

        if (orphanCommits.Count > 0)
        {
            recommendations.Add(new ActionableRecommendation
            {
                Type = RecommendationType.OrphanCommitHandling,
                Title = "Handle Orphaned Commits",
                Description = $"Review {orphanCommits.Count} commits without tickets",
                Priority = RecommendationPriority.Medium
            });
        }

        return recommendations;
    }

    private AnalysisExport BuildExportOptions(string fromBranch, string toBranch)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var baseName = $"cherrypick_{fromBranch.Replace("/", "_")}_to_{toBranch.Replace("/", "_")}_{timestamp}";

        return new AnalysisExport
        {
            BashScript = new ExportableScript { FileName = $"{baseName}.sh" },
            PowerShellScript = new ExportableScript { FileName = $"{baseName}.ps1" },
            JsonReport = new ExportableReport { FileName = $"{baseName}_report.json" },
            MarkdownReport = new ExportableReport { FileName = $"{baseName}_report.md" },
            CsvSummary = new ExportableReport { FileName = $"{baseName}_summary.csv" }
        };
    }

    /// <summary>
    /// Determine ticket priority based on various factors like commit count, file changes, authors, and time span
    /// </summary>
    private static TicketPriority DetermineTicketPriority(string key, List<CpCommit> commits)
    {
        if (commits.Count == 0)
            return TicketPriority.Low;

        // Calculate various metrics
        var commitCount = commits.Count;
        var uniqueFiles = commits.SelectMany(c => c.ModifiedFiles).Distinct().Count();
        var uniqueAuthors = commits.Select(c => c.Author).Distinct().Count();
        var hasMergeCommits = commits.Any(c => c.IsMergeCommit);
        
        // Calculate time span
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);
        
        // Check for critical files
        var criticalFiles = commits.SelectMany(c => c.ModifiedFiles)
            .Where(IsCriticalFile)
            .Distinct()
            .Count();

        // Priority scoring system
        var priorityScore = 0;

        // Commit count factor (more commits = higher priority)
        if (commitCount >= 10) priorityScore += 3;
        else if (commitCount >= 5) priorityScore += 2;
        else if (commitCount >= 3) priorityScore += 1;

        // File count factor (more files = higher priority)
        if (uniqueFiles >= 20) priorityScore += 3;
        else if (uniqueFiles >= 10) priorityScore += 2;
        else if (uniqueFiles >= 5) priorityScore += 1;

        // Author count factor (more authors = higher complexity)
        if (uniqueAuthors >= 3) priorityScore += 2;
        else if (uniqueAuthors >= 2) priorityScore += 1;

        // Time span factor (longer time = potentially more complex)
        if (timeSpan.TotalDays >= 30) priorityScore += 2;
        else if (timeSpan.TotalDays >= 14) priorityScore += 1;

        // Merge commits factor
        if (hasMergeCommits) priorityScore += 1;

        // Critical files factor (highest weight)
        if (criticalFiles >= 3) priorityScore += 4;
        else if (criticalFiles >= 1) priorityScore += 2;

        // Determine priority based on score
        return priorityScore switch
        {
            >= 8 => TicketPriority.High,
            >= 4 => TicketPriority.Medium,
            _ => TicketPriority.Low
        };
    }

    /// <summary>
    /// Check if a file is considered critical (e.g., configuration, build files, core business logic)
    /// </summary>
    private static bool IsCriticalFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        var path = filePath.ToLowerInvariant();

        // Critical file patterns
        var criticalPatterns = new[]
        {
            // Build/Config files
            "package.json", "pom.xml", ".csproj", ".sln", "build.gradle", "webpack.config",
            "tsconfig.json", "appsettings.json", "web.config", "app.config",
            
            // CI/CD files
            ".yml", ".yaml", "dockerfile", "docker-compose",
            
            // Database files
            ".sql", "migration", "schema",
            
            // Security files
            "security", "auth", "authentication", "authorization"
        };

        // Critical directories
        var criticalDirectories = new[]
        {
            "/config/", "/configuration/", "/settings/",
            "/security/", "/auth/", "/authentication/",
            "/database/", "/db/", "/migrations/",
            "/core/", "/kernel/", "/framework/"
        };

        // Check file name patterns
        if (criticalPatterns.Any(pattern => fileName.Contains(pattern)))
            return true;

        // Check directory patterns
        if (criticalDirectories.Any(dir => path.Contains(dir)))
            return true;

        return false;
    }
    private static string GenerateHtmlReport(AnalysisResult result)
    {
        // TODO: This should be injected via DI instead of using static
        var reportGenerator = new ReportGenerator();
        return reportGenerator.GenerateHtmlReport(result);
    }

    private static string GenerateBashScript(AnalysisResult result) => "#!/bin/bash\necho 'Cherry-pick script'";
    private static string GeneratePowerShellScript(AnalysisResult result) => "Write-Host 'Cherry-pick script'";
    private static string GenerateCsvSummary(AnalysisResult result) => "Ticket,Commits,Status\n";
    private static string GenerateMarkdownReport(AnalysisResult result) => "# Cherry-Pick Analysis Report\n";
}

public class ExportableReport
{
    public string FileName { get; set; } = null!;
}

public class ExportableScript
{
    public string FileName { get; set; } = null!;
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
    }
}

public static class StringHelpers
{
    public static string Repeat(this string input, int count)
    {
        return string.Concat(Enumerable.Repeat(input, count));
    }
}
