using CherryPickSmart.Models;
using Spectre.Console;
using static CherryPickSmart.Core.TicketAnalysis.OrphanCommitDetector;
using static CherryPickSmart.Core.Integration.JiraClient; // Add this if TicketInfo is defined here

namespace CherryPickSmart.Services;

public class InteractivePromptService
{
    public List<string> SelectTickets(Dictionary<string, JiraTicket> availableTickets)
    {
        var tree = new Tree("[yellow]Available Tickets[/]");

        var byStatus = availableTickets.GroupBy(t => t.Value.Status);

        foreach (var statusGroup in byStatus)
        {
            var statusNode = tree.AddNode($"[blue]{statusGroup.Key}[/] ({statusGroup.Count()})");

            foreach (var (key, info) in statusGroup)
            {
                var markup = $"[green]{key}[/] - {info.Summary.EscapeMarkup()}";
                if (info.Priority == "High")
                    markup = $"[red]![/] {markup}";

                statusNode.AddNode(markup);
            }
        }

        AnsiConsole.Write(tree);

        var choices = availableTickets.Select(t => 
        {
            var display = $"{t.Key} - {t.Value.Summary}";
            if (t.Value.Status != "Ready for Deployment")
                display += $" [{t.Value.Status}]";
            return display;
        }).ToList();

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select tickets to cherry-pick:")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up and down to reveal more tickets)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoices(choices));

        return selected.Select(s => s.Split(" - ")[0]).ToList();
    }

    public Task<Dictionary<CpCommit, string>> ResolveOrphansAsync(
        List<OrphanCommit> orphans,
        bool autoAcceptHighConfidence = false)
    {
        var assignments = new Dictionary<CpCommit, string>();

        AnsiConsole.Write(new Rule("[red]Orphaned Commits[/]"));
        AnsiConsole.MarkupLine($"Found [yellow]{orphans.Count}[/] commits without ticket references.\n");

        foreach (var orphan in orphans)
        {
            var panel = new Panel(
                $"[yellow]SHA:[/] {orphan.Commit.ShortSha}\n" +
                $"[yellow]Author:[/] {orphan.Commit.Author}\n" +
                $"[yellow]Date:[/] {orphan.Commit.Timestamp:yyyy-MM-dd HH:mm}\n" +
                $"[yellow]Message:[/] {orphan.Commit.Message}\n" +
                $"[yellow]Files:[/] {string.Join(", ", orphan.Commit.ModifiedFiles.Take(3))}...")
                .Header("[red]Orphaned Commit[/]")
                .BorderColor(Color.Red);

            AnsiConsole.Write(panel);

            var highConfidenceSuggestion = orphan.Suggestions.FirstOrDefault(s => s.Confidence >= 80);

            if (autoAcceptHighConfidence && highConfidenceSuggestion != null)
            {
                assignments[orphan.Commit] = highConfidenceSuggestion.TicketKey;
                AnsiConsole.MarkupLine(
                    $"[green]✓ Auto-assigned to {highConfidenceSuggestion.TicketKey}[/] " +
                    $"({highConfidenceSuggestion.Confidence:F0}% confidence - {string.Join(", ", highConfidenceSuggestion.Reasons)})");
                continue;
            }

            if (orphan.Suggestions.Any())
            {
                var table = new Table();
                table.AddColumn("Ticket");
                table.AddColumn("Confidence");
                table.AddColumn("Reasons");

                foreach (var suggestion in orphan.Suggestions.Take(5))
                {
                    var confidenceColor = suggestion.Confidence >= 70 ? "green" : 
                                         suggestion.Confidence >= 40 ? "yellow" : "red";

                    table.AddRow(
                        suggestion.TicketKey,
                        $"[{confidenceColor}]{suggestion.Confidence:F0}%[/]",
                        string.Join(", ", suggestion.Reasons));
                }

                AnsiConsole.Write(table);
            }

            var choices = new List<string> { "Skip this commit" };
            if (orphan.Suggestions.Any())
                choices.AddRange(orphan.Suggestions.Select(s => s.TicketKey));
            choices.Add("Enter ticket manually");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select ticket for this commit:")
                    .AddChoices(choices));

            if (choice == "Enter ticket manually")
            {
                var ticket = AnsiConsole.Ask<string>("Enter ticket (e.g., HSAMED-1234):");
                assignments[orphan.Commit] = ticket; // Fix: Use orphan.Commit directly instead of orphan.Commit.Sha
            }
            else if (choice != "Skip this commit")
            {
                assignments[orphan.Commit] = choice; // Fix: Use orphan.Commit directly instead of orphan.Commit.Sha
            }

            AnsiConsole.WriteLine();
        }

        return Task.FromResult(assignments);
    }
}
