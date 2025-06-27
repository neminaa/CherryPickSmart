using System.Text.Json;
using CherryPickSmart.Core.ConflictAnalysis;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.Integration;
using CherryPickSmart.Core.TicketAnalysis;
using CherryPickSmart.Models;
using CherryPickSmart.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
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

    [Option("output", HelpText = "Save plan to file")]
    public string? OutputFile { get; set; }

        public async Task<int> ExecuteAsync(IServiceProvider services)
        {
            var parser = services.GetRequiredService<GitHistoryParser>();
            var graph = parser.ParseHistory("", FromBranch, ToBranch);
            var targetCommits = parser.GetCommitsInBranch("", ToBranch);

            var ticketExtractor = services.GetRequiredService<TicketExtractor>();
            var ticketMap = ticketExtractor.BuildTicketCommitMap(graph);

        var jiraClient = services.GetRequiredService<JiraClient>();
        var ticketInfos = await jiraClient.GetTicketsBatchAsync(ticketMap.Keys.ToList());

        var orphanDetector = services.GetRequiredService<OrphanCommitDetector>();
        var inferenceEngine = services.GetRequiredService<TicketInferenceEngine>();
        var orphans = orphanDetector.FindOrphans(graph, ticketMap);

        foreach (var orphan in orphans)
        {
            var suggestions = await inferenceEngine.GenerateSuggestionsAsync(orphan, graph, ticketMap);
            orphan.Suggestions.AddRange(suggestions);
        }

        var promptService = services.GetRequiredService<InteractivePromptService>();
        var orphanAssignments = await promptService.ResolveOrphansAsync(orphans, AutoInfer);

        var selectedTickets = promptService.SelectTickets(ticketInfos);

        var selectedCommits = GetCommitsForTickets(selectedTickets, ticketMap, orphanAssignments);
        var conflictPredictor = services.GetRequiredService<ConflictPredictor>();
        var conflicts = conflictPredictor.PredictConflicts(selectedCommits, targetCommits);

        if (conflicts.Any(c => c.Risk >= ConflictRisk.High))
        {
            AnsiConsole.MarkupLine("[red]Warning: High risk conflicts detected![/]");
        }

        var optimizer = services.GetRequiredService<OrderOptimizer>();
        var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
        var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);

        var plan = optimizer.OptimizeOrder(selectedCommits, mergeAnalyses, conflicts);

        DisplayCherryPickPlan(plan);

        if (!string.IsNullOrEmpty(OutputFile))
        {
            var planJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(OutputFile, planJson);
            Console.WriteLine($"\nPlan saved to: {OutputFile}");
        }

        return 0;
    }

    private void DisplayCherryPickPlan(List<CherryPickStep> plan)
    {
        AnsiConsole.MarkupLine("[green]Cherry-Pick Plan:[/]");
        var table = new Table();
        table.AddColumn("Step Type");
        table.AddColumn("Description");
        table.AddColumn("Git Command");

        foreach (var step in plan)
        {
            table.AddRow(
                step.Type.ToString(),
                step.Description,
                $"[blue]{step.GitCommand}[/]");
        }

        AnsiConsole.Write(table);
    }

    private List<Commit> GetCommitsForTickets(
        IEnumerable<string> selectedTickets,
        Dictionary<string, List<string>> ticketMap,
        Dictionary<string, string> orphanAssignments)
    {
        var selectedCommits = new List<Commit>();

        foreach (var ticket in selectedTickets)
        {
            if (ticketMap.TryGetValue(ticket, out var commitShas))
            {
                selectedCommits.AddRange(commitShas.Select(sha => new Commit { Sha = sha }));
            }
        }

        foreach (var (orphanSha, assignedTicket) in orphanAssignments)
        {
            if (selectedTickets.Contains(assignedTicket))
            {
                selectedCommits.Add(new Commit { Sha = orphanSha });
            }
        }

        return selectedCommits;
    }
}
