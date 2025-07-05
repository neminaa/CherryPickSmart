using CherryPickAnalyzer.Models;
using Spectre.Console;

namespace CherryPickAnalyzer.Display;

public class RepositoryInfoDisplay
{
    private readonly string _repoPath;
    private readonly RepositoryStatus _status;

    public RepositoryInfoDisplay(string repoPath, RepositoryStatus status)
    {
        _repoPath = repoPath;
        _status = status;
    }

    public void DisplayRepositoryInfo(string currentBranch, IEnumerable<string> remotes)
    {
        var panel = new Panel(new Markup($"""
            [bold]Repository:[/] {_repoPath}
            [bold]Current Branch:[/] {currentBranch}
            [bold]Remotes:[/] {string.Join(", ", remotes)}
            """))
            .Header("📁 Repository Information")
            .BorderColor(Color.Blue);

        AnsiConsole.Write(panel);
    }

    public void DisplayRepositoryStatus()
    {
        if (!_status.HasUncommittedChanges)
        {
            AnsiConsole.MarkupLine("[green]✅ Working directory clean[/]");
            return;
        }

        var table = new Table()
            .AddColumn("Status")
            .AddColumn("Files")
            .BorderColor(Color.Yellow);

        if (_status.ModifiedFiles.Count != 0)
        {
            table.AddRow("🔄 Modified", string.Join(", ", _status.ModifiedFiles.Take(5)) +
                        (_status.ModifiedFiles.Count > 5 ? $" and {_status.ModifiedFiles.Count - 5} more..." : ""));
        }

        if (_status.StagedFiles.Count != 0)
        {
            table.AddRow("📝 Staged", string.Join(", ", _status.StagedFiles.Take(5)) +
                        (_status.StagedFiles.Count > 5 ? $" and {_status.StagedFiles.Count - 5} more..." : ""));
        }

        if (_status.UntrackedFiles.Count != 0)
        {
            table.AddRow("❓ Untracked", string.Join(", ", _status.UntrackedFiles.Take(5)) +
                        (_status.UntrackedFiles.Count > 5 ? $" and {_status.UntrackedFiles.Count - 5} more..." : ""));
        }

        AnsiConsole.Write(new Panel(table)
            .Header("⚠️  Uncommitted Changes")
            .BorderColor(Color.Yellow));
    }
}
