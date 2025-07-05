using System;
using System.Collections.Generic;
using System.Linq;
using GitCherryHelper.Models;
using Spectre.Console;

namespace GitCherryHelper;

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

        if (!analysis.HasContentDifferences || analysis.ContentAnalysis.ChangedFiles.Count == 0)
        {
            return;
        }
        var fileTable = new Table()
            .AddColumn("Status")
            .AddColumn("File")
            .AddColumn("Changes")
            .BorderColor(Color.Yellow);

        foreach (var file in analysis.ContentAnalysis.ChangedFiles.Take(15))
        {
            var statusIcon = file.Status switch
            {
                "Added" => "[green]➕[/]",
                "Modified" => "[yellow]🔄[/]",
                "Deleted" => "[red]➖[/]",
                "Renamed" => "[blue]📝[/]",
                _ => "[dim]❓[/]"
            };

            fileTable.AddRow(
                statusIcon,
                file.NewPath,
                $"[green]+{file.LinesAdded}[/] [red]-{file.LinesDeleted}[/]");
        }

        var statsRow =
            "📂 File Changes" +
            $"📁 {analysis.ContentAnalysis.Stats.FilesChanged} files, " +
            $"[green]+{analysis.ContentAnalysis.Stats.LinesAdded}[/] " +
            $"[red]-{analysis.ContentAnalysis.Stats.LinesDeleted}[/] lines";

        AnsiConsole.Write(new Panel(fileTable)
            .Header(statsRow)
            .BorderColor(Color.Yellow));
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
}
