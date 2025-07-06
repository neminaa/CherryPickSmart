using CherryPickAnalyzer.Models;
using Spectre.Console;

namespace CherryPickAnalyzer.Display;

public class RepositoryInfoDisplay(string repoPath, RepositoryStatus status)
{
    public void DisplayRepositoryInfo(string currentBranch, IEnumerable<string> remotes)
    {
        var panel = new Panel(new Markup($"""
            [bold]Repository:[/] {repoPath}
            [bold]Current Branch:[/] {currentBranch}
            [bold]Remotes:[/] {string.Join(", ", remotes)}
            """))
            .Header("📁 Repository Information")
            .BorderColor(Color.Blue);

        AnsiConsole.Write(panel);
    }

    public void DisplayRepositoryStatus()
    {
        if (!status.HasUncommittedChanges)
        {
            AnsiConsole.MarkupLine("[green]✅ Working directory clean[/]");
            return;
        }

        var table = new Table()
            .AddColumn("Status")
            .AddColumn("Files")
            .BorderColor(Color.Yellow);

        if (status.ModifiedFiles.Count != 0)
        {
            table.AddRow("🔄 Modified", string.Join(", ", status.ModifiedFiles.Take(5)) +
                        (status.ModifiedFiles.Count > 5 ? $" and {status.ModifiedFiles.Count - 5} more..." : ""));
        }

        if (status.StagedFiles.Count != 0)
        {
            table.AddRow("📝 Staged", string.Join(", ", status.StagedFiles.Take(5)) +
                        (status.StagedFiles.Count > 5 ? $" and {status.StagedFiles.Count - 5} more..." : ""));
        }

        if (status.UntrackedFiles.Count != 0)
        {
            table.AddRow("❓ Untracked", string.Join(", ", status.UntrackedFiles.Take(5)) +
                        (status.UntrackedFiles.Count > 5 ? $" and {status.UntrackedFiles.Count - 5} more..." : ""));
        }

        AnsiConsole.Write(new Panel(table)
            .Header("⚠️  Uncommitted Changes")
            .BorderColor(Color.Yellow));
    }
}
