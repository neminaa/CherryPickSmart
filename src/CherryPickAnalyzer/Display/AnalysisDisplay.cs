using CherryPickAnalyzer.Models;
using Spectre.Console;

namespace CherryPickAnalyzer.Display;

public class AnalysisDisplay
{
    public static void DisplaySuggestions(DeploymentAnalysis analysis, string targetBranch)
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
}
