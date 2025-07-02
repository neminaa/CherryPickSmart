using CherryPickSmart.Core.ConflictAnalysis;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.Integration;
using CherryPickSmart.Core.TicketAnalysis;
using CherryPickSmart.Models;
using CherryPickSmart.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Text.Json;
using static CherryPickSmart.Core.ConflictAnalysis.ConflictPredictor;

namespace CherryPickSmart.Commands;

[Verb("plan", HelpText = "Create interactive cherry-pick plan")]
public class PlanCommand : ICommand
{
    [Option('f', "from", Required = true)]
    public string FromBranch { get; set; } = "";

    [Option('t', "to", Required = true)]
    public string ToBranch { get; set; } = "";

    [Option("auto-infer", HelpText = "Automatically accept high-confidence inferences")]
    public bool AutoInfer { get; set; }
    
    [Option('r', "repo", Required = true, HelpText = "Path to the repository")]
    public string RepositoryPath { get; set; } = "";

    [Option("output-dir", HelpText = "Directory for output files")]
    public string? OutputDirectory { get; set; }

    public async Task<int> ExecuteAsync(IServiceProvider services)
    {
        try
        {
            // Welcome header
            var rule = new Rule("[bold cyan]CherryPick Smart - Interactive Planning[/]")
            {
                Style = Style.Parse("cyan")
            };
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();

            // Display parameters
            var paramTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn("[bold]Parameter[/]").Centered())
                .AddColumn(new TableColumn("[bold]Value[/]").LeftAligned());

            paramTable.AddRow("[cyan]Source Branch[/]", $"[yellow]{FromBranch}[/]");
            paramTable.AddRow("[cyan]Target Branch[/]", $"[yellow]{ToBranch}[/]");
            paramTable.AddRow("[cyan]Repository[/]", $"[dim]{RepositoryPath}[/]");
            paramTable.AddRow("[cyan]Auto-Infer[/]", AutoInfer ? "[green]Yes[/]" : "[red]No[/]");
            if (!string.IsNullOrEmpty(OutputDirectory))
            {
                paramTable.AddRow("[cyan]Output Directory[/]", $"[dim]{OutputDirectory}[/]");
            }

            AnsiConsole.Write(paramTable);
            AnsiConsole.WriteLine();

            CpCommitGraph graph = null!;
            HashSet<string> targetCommits = null!;
            Dictionary<string, List<CpCommit>> ticketMap = null!;
            Dictionary<string, JiraClient.JiraTicket> ticketInfos = null!;
            List<OrphanCommitDetector.OrphanCommit> orphans = null!;
            List<CpCommit> selectedCommits = null!;
            List<ConflictPrediction> conflicts = null!;
            List<CherryPickStep> plan = null!;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("green"))
                .StartAsync("Creating cherry-pick plan...", async ctx =>
                {
                    // Step 1: Parse Git History
                    ctx.Status("📊 Parsing Git history...");
                    var parser = services.GetRequiredService<GitHistoryParser>();
                    graph = parser.ParseHistory(RepositoryPath, FromBranch, ToBranch);
                    targetCommits = parser.GetCommitsInBranch(RepositoryPath, ToBranch);

                    // Step 2: Extract Tickets
                    ctx.Status("🎫 Extracting tickets from commit messages...");
                    var ticketExtractor = services.GetRequiredService<TicketExtractor>();
                    ticketMap = ticketExtractor.BuildTicketCommitMap(graph);

                    // Step 3: Fetch ticket information from JIRA
                    ctx.Status("🔗 Fetching ticket information from JIRA...");
                    var jiraClient = services.GetRequiredService<JiraClient>();
                    ticketInfos = await jiraClient.GetTicketsBatchAsync(ticketMap.Keys.ToList());

                    // Step 4: Detect and process orphan commits
                    ctx.Status("🏴 Detecting orphaned commits...");
                    var orphanDetector = services.GetRequiredService<OrphanCommitDetector>();
                    var inferenceEngine = services.GetRequiredService<TicketInferenceEngine>();
                    orphans = orphanDetector.FindOrphans(graph, ticketMap);

                    if (orphans.Any())
                    {
                        ctx.Status($"🤖 Generating suggestions for {orphans.Count} orphan commits...");
                        foreach (var orphan in orphans)
                        {
                            var suggestions = await inferenceEngine.GenerateSuggestionsAsync(orphan, graph, ticketMap);
                            orphan.Suggestions.AddRange(suggestions);
                        }
                    }
                });

            // Interactive sections
            AnsiConsole.WriteLine();
            
            // Process orphan assignments
            Dictionary<CpCommit, string> orphanAssignments = new();
            if (orphans.Any())
            {
                var orphanPanel = new Panel(new Markup($"[yellow]Found {orphans.Count} orphaned commits without ticket associations.[/]\n" +
                                                      $"[dim]These commits need to be assigned to tickets for proper tracking.[/]"))
                    .Header("[yellow]🏴 Orphan Commits[/]")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Yellow);
                AnsiConsole.Write(orphanPanel);
                AnsiConsole.WriteLine();

                var promptService = services.GetRequiredService<InteractivePromptService>();
                orphanAssignments = await promptService.ResolveOrphansAsync(orphans, AutoInfer);
            }

            // Select tickets
            AnsiConsole.WriteLine();
            var ticketPanel = new Panel(new Markup($"[green]Found {ticketMap.Count} tickets with {ticketMap.Values.Sum(v => v.Count)} total commits.[/]\n" +
                                                  "[dim]Select which tickets to include in the cherry-pick plan.[/]"))
                .Header("[green]🎫 Ticket Selection[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green);
            AnsiConsole.Write(ticketPanel);
            AnsiConsole.WriteLine();

            var promptService2 = services.GetRequiredService<InteractivePromptService>();
            var selectedTickets = promptService2.SelectTickets(ticketInfos);


            // Get commits for selected tickets
            AnsiConsole.WriteLine("📋 Gathering commits for selected tickets...");
                    selectedCommits = GetCommitsForTickets(selectedTickets, ticketMap, orphanAssignments);

            // Predict conflicts
            AnsiConsole.WriteLine("⚠️  Predicting potential conflicts...");
                    var conflictPredictor = services.GetRequiredService<ConflictPredictor>();
                    conflicts = conflictPredictor.PredictConflicts(RepositoryPath, selectedCommits, ToBranch);

            // Optimize order
            AnsiConsole.WriteLine("🔧 Optimizing cherry-pick order...");
                    var optimizer = services.GetRequiredService<OrderOptimizer>();
                    var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
                    var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);
                    plan = optimizer.OptimizeOrder(selectedCommits, mergeAnalyses, conflicts);
                

            // Display conflict warnings
            var highRiskConflicts = conflicts.Where(c => c.Risk >= ConflictRisk.High).ToList();
            if (highRiskConflicts.Any())
            {
                AnsiConsole.WriteLine();
                var conflictTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Red)
                    .AddColumn(new TableColumn("[bold]File[/]").LeftAligned())
                    .AddColumn(new TableColumn("[bold]Risk Level[/]").Centered())
                    .AddColumn(new TableColumn("[bold]Type[/]").Centered())
                    .AddColumn(new TableColumn("[bold]Conflicting Commits[/]").LeftAligned());

                foreach (var conflict in highRiskConflicts.Take(10))
                {
                    var riskColor = conflict.Risk switch
                    {
                        ConflictRisk.Certain => "red",
                        ConflictRisk.High => "orange1",
                        _ => "yellow"
                    };

                    conflictTable.AddRow(
                        $"[white]{conflict.File}[/]",
                        $"[{riskColor}]{conflict.Risk}[/]",
                        $"[dim]{conflict.Type}[/]",
                        $"[dim]{conflict.ConflictingCommits.Count} commits[/]"
                    );
                }

                var conflictPanel = new Panel(conflictTable)
                    .Header($"[bold red]⚠️  High Risk Conflicts Detected ({highRiskConflicts.Count} total)[/]")
                    .Border(BoxBorder.Heavy)
                    .BorderColor(Color.Red);

                AnsiConsole.Write(conflictPanel);
                AnsiConsole.WriteLine();
            }

            // Display the plan
            AnsiConsole.WriteLine();
            DisplayCherryPickPlan(plan, selectedCommits.Count, conflicts.Count);

            // Save plan if requested
            if (!string.IsNullOrEmpty(OutputDirectory))
            {
                var outputDir = OutputDirectory;
                Directory.CreateDirectory(outputDir);
                
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"cherrypick_plan_{FromBranch.Replace("/", "_")}_to_{ToBranch.Replace("/", "_")}_{timestamp}.json";
                var outputPath = Path.Combine(outputDir, fileName);

                AnsiConsole.WriteLine();
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("blue"))
                    .StartAsync("Saving plan...", async ctx =>
                    {
                        var planJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(outputPath, planJson);
                    });

                var savePanel = new Panel(new Markup($"[green]✅ Plan saved successfully![/]\n[dim]File: {outputPath}[/]"))
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Green);
                AnsiConsole.Write(savePanel);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold green]✅ Cherry-pick plan created successfully![/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold red]❌ Error creating cherry-pick plan[/]");

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

    private void DisplayCherryPickPlan(List<CherryPickStep> plan, int commitCount, int conflictCount)
    {
        // Plan summary
        var summaryPanel = new Panel(new Markup($"[bold white]Cherry-Pick Execution Plan[/]\n" +
                                              $"[cyan]Total Steps:[/] [yellow]{plan.Count}[/]\n" +
                                              $"[cyan]Commits:[/] [yellow]{commitCount}[/]\n" +
                                              $"[cyan]Potential Conflicts:[/] [{(conflictCount > 0 ? "red" : "green")}]{conflictCount}[/]"))
            .Header("[bold blue]📋 Plan Summary[/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Blue);

        AnsiConsole.Write(summaryPanel);
        AnsiConsole.WriteLine();

        // Detailed plan table
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold]#[/]").Centered())
            .AddColumn(new TableColumn("[bold]Step Type[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Git Command[/]").LeftAligned());

        var stepNumber = 1;
        foreach (var step in plan)
        {
            var typeColor = step.Type switch
            {
                StepType.SingleCommit => "green",
                StepType.MergeCommit => "blue",
                StepType.CommitRange => "yellow",
                _ => "white"
            };

            var typeIcon = step.Type switch
            {
                StepType.SingleCommit => "🍒",
                StepType.MergeCommit => "🔀",
                StepType.CommitRange => "📦",
                _ => "📌"
            };

            table.AddRow(
                $"[bold]{stepNumber++}[/]",
                $"[{typeColor}]{typeIcon} {step.Type}[/]",
                $"[white]{step.Description}[/]",
                $"[cyan mono]{step.GitCommand}[/]"
            );
        }

        AnsiConsole.Write(table);

        // Instructions
        AnsiConsole.WriteLine();
        var instructionPanel = new Panel(new Markup("[dim]To execute this plan, run the Git commands in order.\n" +
                                                   "If conflicts occur, resolve them before proceeding to the next step.[/]"))
            .Header("[dim]Instructions[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);
        AnsiConsole.Write(instructionPanel);
    }

    private List<CpCommit> GetCommitsForTickets(
        List<string> selectedTickets,
        Dictionary<string, List<CpCommit>> ticketMap,
        Dictionary<CpCommit, string> orphanAssignments)
    {
        var selectedCommits = new List<CpCommit>();

        var tickets = selectedTickets.ToArray();
        foreach (var ticket in tickets)
        {
            if (ticketMap.TryGetValue(ticket, out var commitShas))
            {
                selectedCommits.AddRange(commitShas);
            }
        }

        foreach (var (orphanSha, assignedTicket) in orphanAssignments)
        {
            if (tickets.Contains(assignedTicket))
            {
                selectedCommits.Add(orphanSha);
            }
        }

        return selectedCommits;
    }
}
