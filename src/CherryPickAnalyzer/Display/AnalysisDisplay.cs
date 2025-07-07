using CherryPickAnalyzer.Models;
using Spectre.Console;

namespace CherryPickAnalyzer.Display;

public class AnalysisDisplay
{
    public static void DisplaySuggestions(DeploymentAnalysis analysis,string srcBranch, string targetBranch)
    {
        var suggestions = new List<string>();

        AnsiConsole.WriteLine();

        if (analysis.HasContentDifferences)
        {
            suggestions.Add($"[blue]View detailed diff:[/] git diff {targetBranch}..{srcBranch}");
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
        AnsiConsole.WriteLine();

    }
}
