using CherryPickAnalyzer.Helpers;
using CherryPickAnalyzer.Models;
using Spectre.Console;

namespace CherryPickAnalyzer.Display;

public class AnalysisDisplay
{
    public void DisplayAnalysisAsTable(DeploymentAnalysis analysis, string sourceBranch, string targetBranch)
    {
        var summaryTable = new Table()
            .AddColumn("Metric")
            .AddColumn("Count")
            .AddColumn("Status")
            .BorderColor(Color.Blue);

        summaryTable.AddRow("Outstanding Commits",
            analysis.OutstandingCommits.Count.ToString(),
            analysis.OutstandingCommits.Count != 0 ? "[red]Needs attention[/]" : "[green]✅ Up to date[/]");

        summaryTable.AddRow("New Commits to Cherry-pick",
            analysis.CherryPickAnalysis.NewCommits.Count.ToString(),
            analysis.CherryPickAnalysis.NewCommits.Count != 0 ? "[yellow]Ready to apply[/]" : "[green]✅ None[/]");

        summaryTable.AddRow("Already Applied Commits",
            analysis.CherryPickAnalysis.AlreadyAppliedCommits.Count.ToString(),
            analysis.CherryPickAnalysis.AlreadyAppliedCommits.Count != 0
                ? "[green]✅ Up to date[/]"
                : "[red]Needs attention[/]");

        summaryTable.AddRow("Content Differences",
            analysis.HasContentDifferences ? "Yes" : "No",
            analysis.HasContentDifferences ? "[red]Different[/]" : "[green]✅ Same[/]");

        AnsiConsole.Write(new Panel(summaryTable)
            .Header($"📊 Analysis Summary: {targetBranch} → {sourceBranch}")
            .BorderColor(Color.Blue));

        if (analysis.OutstandingCommits.Count != 0)
        {
            var commitsTable = new Table()
                .AddColumn("SHA")
                .AddColumn("Message")
                .AddColumn("Author")
                .AddColumn("Date")
                .BorderColor(Color.Red);

            foreach (var commit in analysis.OutstandingCommits.Take(20))
            {
                commitsTable.AddRow(
                    $"[dim]{commit.ShortSha}[/]",
                    commit.Message.Length > 50 ? string.Concat(commit.Message.AsSpan(0, 47), "...") : commit.Message,
                    commit.Author,
                    commit.Date.ToString("MMM dd HH:mm"));
            }

            if (analysis.OutstandingCommits.Count > 20)
            {
                commitsTable.AddRow("[dim]...[/]", $"[dim]and {analysis.OutstandingCommits.Count - 20} more commits[/]", "", "");
            }

            AnsiConsole.Write(new Panel(commitsTable)
                .Header("📋 Outstanding Commits")
                .BorderColor(Color.Red));
        }

        if (analysis.HasContentDifferences && analysis.ContentAnalysis.ChangedFiles.Count > 0)
        {
            // Show a summary of file changes since we already displayed them in real-time
            var statsRow = $"📂 File Changes Summary: 📁 {analysis.ContentAnalysis.Stats.FilesChanged} files, " +
                          $"[green]+{analysis.ContentAnalysis.Stats.LinesAdded}[/] " +
                          $"[red]-{analysis.ContentAnalysis.Stats.LinesDeleted}[/] lines";

            AnsiConsole.Write(new Panel(statsRow)
                .Header("📊 Content Analysis Complete")
                .BorderColor(Color.Yellow));
        }
    }

    public void DisplaySuggestions(DeploymentAnalysis analysis, string targetBranch)
    {
        var suggestions = new List<string>();

        if (analysis.CherryPickAnalysis.NewCommits.Count != 0)
        {
            suggestions.Add($"[yellow]Run cherry-pick command:[/] git-deploy cherry-pick -s deploy/dev -t {targetBranch}");
        }

        if (analysis.HasContentDifferences)
        {
            suggestions.Add($"[blue]View detailed diff:[/] git diff {targetBranch}..deploy/dev");
        }

        if (analysis.OutstandingCommits.Count == 0)
        {
            suggestions.Add("[green]✅ Branches are in sync - ready for deployment![/]");
        }

        if (suggestions.Count != 0)
        {
            AnsiConsole.Write(new Panel(string.Join("\n", suggestions))
                .Header("💡 Suggestions")
                .BorderColor(Color.Magenta1));
        }
    }

    public void DisplayAnalysisAsJson(DeploymentAnalysis analysis)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        AnsiConsole.WriteLine(json);
    }

    public void DisplayAnalysisAsMarkdown(DeploymentAnalysis analysis, string sourceBranch, string targetBranch)
    {
        var markdown = $"""
            # Deployment Analysis: {targetBranch} → {sourceBranch}

            ## Summary
            - Outstanding Commits: {analysis.OutstandingCommits.Count}
            - New Commits to Cherry-pick: {analysis.CherryPickAnalysis.NewCommits.Count}
            - Content Differences: {(analysis.HasContentDifferences ? "Yes" : "No")}

            ## Outstanding Commits
            {string.Join("\n", analysis.OutstandingCommits.Take(10).Select(c => $"- `{c.ShortSha}` {c.Message} ({c.Author})"))}

            ## Cherry-pick Commands
            ```bash
            {string.Join("\n", CherryPickHelper.GenerateCherryPickCommands(targetBranch, analysis.CherryPickAnalysis.NewCommits))}
            ```
            """;

        AnsiConsole.WriteLine(markdown);
    }

    public void DisplayTicketMultiSelect(Dictionary<string, CherryPickHelper.JiraTicketInfo> ticketInfos, List<string> allTickets)
    {
        if (!ticketInfos.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No Jira tickets found to display[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("Ticket Selection Interface").RuleStyle("blue"));

        // Group tickets by status for better organization
        var ticketsByStatus = ticketInfos.Values
            .GroupBy(t => t.Status)
            .OrderBy(g => GetStatusPriority(g.Key))
            .ToList();

        foreach (var statusGroup in ticketsByStatus)
        {
            var statusIcon = GetStatusIcon(statusGroup.Key);
            var statusColor = GetStatusColor(statusGroup.Key);
            
            AnsiConsole.Write(new Panel($"[{statusColor}]{statusIcon} {statusGroup.Key}[/]")
                .BorderColor(Color.Blue));

            foreach (var ticket in statusGroup.OrderBy(t => t.Key))
            {
                var commitCount = allTickets.Count(t => t == ticket.Key);
                var summary = ticket.Summary.Length > 60 ? ticket.Summary.Substring(0, 57) + "..." : ticket.Summary;
                
                var ticketInfo = $"🎫 {ticket.Key} | Status: {ticket.Status} | Commits: {commitCount}\n" +
                                $"   Summary: {summary}";
                
                AnsiConsole.WriteLine(ticketInfo);
            }
            
            AnsiConsole.WriteLine();
        }

        // Add filter controls
        AnsiConsole.Write(new Panel(
            "Filters:\n" +
            "• Status: Filter by ticket status\n" +
            "• Dependencies: Auto-select required tickets\n" +
            "• Search: Filter by ticket key or summary"
        ).BorderColor(Color.Yellow));

        // Add selection controls
        AnsiConsole.Write(new Panel(
            "Selection:\n" +
            "• Select All: Choose all visible tickets\n" +
            "• Select Ready: Select tickets ready for deployment\n" +
            "• Generate Commands: Create cherry-pick commands for selected tickets"
        ).BorderColor(Color.Green));
    }

    public List<CherryPickHelper.JiraTicketInfo> DisplayTicketMultiSelectInteractive(Dictionary<string, CherryPickHelper.JiraTicketInfo> ticketInfos, List<string> allTickets)
    {
        if (!ticketInfos.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No Jira tickets found to display[/]");
            return new List<CherryPickHelper.JiraTicketInfo>();
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("Interactive Ticket Selection").RuleStyle("blue"));

        // Create the interactive prompt
        var prompt = new MultiSelectionPrompt<CherryPickHelper.JiraTicketInfo>()
            .Title("Select tickets to include in cherry-pick:")
            .PageSize(15)
            .UseConverter(ticket => 
            {
                var commitCount = allTickets.Count(t => t == ticket.Key);
                var statusIcon = GetStatusIcon(ticket.Status);
                var summary = ticket.Summary.Length > 50 ? ticket.Summary.Substring(0, 47) + "..." : ticket.Summary;
                // Use Markup.Escape to properly escape all markup characters
                var safeTicketKey = Markup.Escape(ticket.Key);
                var safeStatus = Markup.Escape(ticket.Status);
                var safeSummary = Markup.Escape(summary);
                return $"{statusIcon} {safeTicketKey} | {safeStatus} | {commitCount} commits | {safeSummary}";
            })
            .AddChoices(ticketInfos.Values.OrderBy(t => GetStatusPriority(t.Status)).ThenBy(t => t.Key));

        var selectedTickets = AnsiConsole.Prompt(prompt);
        
        // Display summary of selected tickets
        if (selectedTickets.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel($"Selected {selectedTickets.Count} tickets for cherry-pick")
                .BorderColor(Color.Green));
            
            foreach (var ticket in selectedTickets)
            {
                var commitCount = allTickets.Count(t => t == ticket.Key);
                AnsiConsole.MarkupLine($"[green]✅[/] {ticket.Key} ({commitCount} commits)");
            }
        }
        else
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel("No tickets selected")
                .BorderColor(Color.Yellow));
        }

        return selectedTickets;
    }

    private static string GetStatusIcon(string status)
    {
        return status.ToLower() switch
        {
            "to do" => "📋",
            "in progress" => "🔄",
            "pending prod deployment" => "⏳",
            "prod deployed" => "✅",
            "done" => "✅",
            "closed" => "🔒",
            _ => "❓"
        };
    }

    private static string GetStatusColor(string status)
    {
        return status.ToLower() switch
        {
            "to do" => "blue",
            "in progress" => "yellow",
            "pending prod deployment" => "red",
            "prod deployed" => "green",
            "done" => "green",
            "closed" => "grey",
            _ => "white"
        };
    }

    private static int GetStatusPriority(string status)
    {
        return status.ToLower() switch
        {
            "to do" => 1,
            "in progress" => 2,
            "pending prod deployment" => 3,
            "prod deployed" => 4,
            "done" => 5,
            "closed" => 6,
            _ => 99
        };
    }
}
