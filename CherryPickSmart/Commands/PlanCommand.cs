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

                    if (orphans.Count > 0)
                    {
                        ctx.Status($"🤖 Generating suggestions for {orphans.Count} orphan commits...");

                        var mergeAnalyzer = new MergeCommitAnalyzer();
                        var analysis = mergeAnalyzer.AnalyzeMerges(graph, []);

                        foreach (var orphan in orphans)
                        {
                            var suggestions = await inferenceEngine.GenerateSuggestionsAsync(analysis, orphan, graph, ticketMap);
                            orphan.Suggestions.AddRange(suggestions);
                        }
                    }
                });

            // Interactive sections
            AnsiConsole.WriteLine();

            // Process orphan assignments
            Dictionary<CpCommit, string> orphanAssignments = new();
            if (orphans.Count > 0)
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
            var selectedCommits = GetCommitsForTickets(selectedTickets, ticketMap, orphanAssignments);

            // Predict conflicts
            var conflicts = await ConflictPredictorIntegration.PredictConflictsWithStatusAsync(
                RepositoryPath, 
                selectedCommits, 
                ToBranch,
                new ConflictPredictorOptions
                {
                    MaxDegreeOfParallelism = 4
                });
            
            // Detect empty commits
            Dictionary<string, EmptyCommitInfo> emptyCommits = null!;
            EmptyCommitDetectionResult emptyDetectionResult;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow"))
                .StartAsync("🔍 Detecting empty commits...", async ctx =>
                {
                    var progress = new Progress<DetectionProgress>(p =>
                    {
                        ctx.Status($"🔍 Detecting empty commits... {p.PercentComplete:F0}% ({p.ProcessedCommits}/{p.TotalCommits})");
                    });

                    var emptyDetector = new EmptyCommitDetector(RepositoryPath, new EmptyCommitDetectorOptions
                    {
                        UseParallelProcessing = selectedCommits.Count > 20, // Only use parallel for larger sets
                        MaxDegreeOfParallelism = 4
                    });

                    emptyDetectionResult = await emptyDetector.DetectEmptyCommitsParallelAsync(
                        selectedCommits,
                        ToBranch,
                        progress);

                    emptyCommits = emptyDetectionResult.EmptyCommits;
                });

            // Optimize order
            AnsiConsole.WriteLine("🔧 Optimizing cherry-pick order...");
            var optimizer = services.GetRequiredService<OrderOptimizer>();
            var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
            var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);
            var plan = optimizer.OptimizeOrder(selectedCommits, mergeAnalyses, conflicts, FromBranch);

            // Mark empty commits in the plan
            foreach (var step in plan)
            {
                if (step.CommitShas.Count == 1)
                {
                    var commitSha = step.CommitShas[0];
                    if (emptyCommits.TryGetValue(commitSha, out var emptyInfo))
                    {
                        step.IsEmpty = true;
                        step.EmptyReason = emptyInfo.Details;
                        step.AlternativeCommand = $"{step.GitCommand} --allow-empty # {emptyInfo.Reason}";
                    }
                }
            }


            // Display conflict warnings
            var highRiskConflicts = conflicts.Where(c => c.Risk >= ConflictRisk.High)
                .OrderBy(o => o.Risk)
                .ThenByDescending(o => o.ConflictingCommits.Count)
                .ToList();

            if (highRiskConflicts.Count > 0)
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
                    .StartAsync("Saving plan...", async _ =>
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
        // Count empty commits
        var emptyCount = plan.Count(p => p.IsEmpty);

        // Interactive toggle for showing/hiding empty commits
        var showEmptyCommits = true;

        while (true)
        {
            // Clear previous display
            //AnsiConsole.Clear();

            // Filter plan based on toggle setting
            var displayPlan = showEmptyCommits ? plan : plan.Where(p => !p.IsEmpty).ToList();
            var hiddenCount = plan.Count - displayPlan.Count;

            // Plan summary with toggle info
            var summaryText = $"[bold white]Cherry-Pick Execution Plan[/]\n" +
                             $"[cyan]Total Steps:[/] [yellow]{plan.Count}[/]" +
                             (hiddenCount > 0 ? $" ([dim]{hiddenCount} hidden[/])" : "") + "\n" +
                             $"[cyan]Commits:[/] [yellow]{commitCount}[/]\n" +
                             $"[cyan]Potential Conflicts:[/] [{(conflictCount > 0 ? "red" : "green")}]{conflictCount}[/]\n" +
                             $"[cyan]Empty Commits:[/] [{(emptyCount > 0 ? "yellow" : "green")}]{emptyCount}[/]" +
                             (emptyCount > 0 ? $" ([{(showEmptyCommits ? "green" : "yellow")}]{(showEmptyCommits ? "shown" : "hidden")}[/])" : "");

            var summaryPanel = new Panel(new Markup(summaryText))
                .Header("[bold blue]📋 Plan Summary[/]")
                .Border(BoxBorder.Double)
                .BorderColor(Color.Blue);

            AnsiConsole.Write(summaryPanel);
            AnsiConsole.WriteLine();

            // Toggle controls info
            var controlsText = $"[dim]Controls: [yellow]T[/] = Toggle empty commits • [yellow]C[/] = Continue • [yellow]Q[/] = Quit view[/]";
            if (emptyCount > 0)
            {
                controlsText += $"\n[dim]Empty commits are currently [yellow]{(showEmptyCommits ? "SHOWN" : "HIDDEN")}[/][/]";
            }

            var controlsPanel = new Panel(new Markup(controlsText))
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey);
            AnsiConsole.Write(controlsPanel);
            AnsiConsole.WriteLine();

            // Detailed plan table
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn(new TableColumn("[bold]#[/]").Centered())
                .AddColumn(new TableColumn("[bold]Step Type[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Git Command[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Status[/]").Centered());

            var stepNumber = 1;
            var displayStepNumber = 1;

            foreach (var step in plan)
            {
                // Skip empty commits if they're hidden
                if (!showEmptyCommits && step.IsEmpty)
                {
                    stepNumber++;
                    continue;
                }

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

                var status = step.IsEmpty
                    ? "[yellow]⚠ Empty[/]"
                    : "[green]✓[/]";

                var command = step.IsEmpty && !string.IsNullOrEmpty(step.AlternativeCommand)
                    ? $"[dim]{step.AlternativeCommand}[/]"
                    : $"[cyan]{step.GitCommand}[/]";

                var description = step.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First();

                // Show original step number and display step number if different
                var stepDisplay = showEmptyCommits || !step.IsEmpty
                    ? $"[bold]{stepNumber}[/]"
                    : $"[bold]{displayStepNumber}[/][dim]({stepNumber})[/]";

                table.AddRow(
                    stepDisplay,
                    $"[{typeColor}]{typeIcon} {step.Type}[/]",
                    $"[white]{description}[/]" + (step.IsEmpty ? $"\n[dim yellow]{step.EmptyReason}[/]" : ""),
                    command,
                    status
                );

                stepNumber++;
                displayStepNumber++;
            }

            AnsiConsole.Write(table);

            // Interactive controls
            AnsiConsole.WriteLine();

            if (emptyCount > 0)
            {
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Choose an action:[/]")
                        .AddChoices(new[]
                        {
                        showEmptyCommits ? "Hide empty commits" : "Show empty commits",
                        "Continue with plan",
                        "Exit view"
                        }));

                switch (choice)
                {
                    case var c when c.Contains("Hide empty") || c.Contains("Show empty"):
                        showEmptyCommits = !showEmptyCommits;
                        continue;

                    case "Continue with plan":
                        break;

                    case "Exit view":
                        return;
                }
            }
            else
            {
                // No empty commits, just show continue option
                if (!AnsiConsole.Confirm("[yellow]Continue with plan?[/]"))
                {
                    return;
                }
            }

            break; // Exit the loop to continue with the rest of the method
        }

        // Instructions (shown after user chooses to continue)
        AnsiConsole.WriteLine();
        var instructions = "[dim]To execute this plan:\n" +
                         "1. Run the Git commands in order\n" +
                         "2. Empty commits can be skipped with 'git cherry-pick --skip'\n" +
                         "3. If conflicts occur, resolve them before proceeding\n";

        if (emptyCount > 0)
        {
            instructions += $"\n[yellow]Note: {emptyCount} commits are expected to be empty and can be safely skipped.[/]\n";
        }

        instructions += $"\n[cyan]Or use the provided PowerShell script:[/]\n" +
                       "[white]  .\\execute_cherrypick_with_empty_handling.ps1 -PlanFile <plan.json> -SkipEmpty[/]";

        var instructionPanel = new Panel(new Markup(instructions))
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
