using CherryPickAnalyzer.Models;
using CliWrap;
using CliWrap.Buffered;
using Spectre.Console;

namespace CherryPickAnalyzer.Core;

public class GitCommandExecutor(string repoPath)
{
    private readonly Command _gitCommand = Cli.Wrap("git")
        .WithWorkingDirectory(repoPath)
        .WithValidation(CommandResultValidation.None);

    public async Task FetchWithProgressAsync(string remote, CancellationToken cancellationToken)
    {
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Fetching from {remote}[/]");
                task.IsIndeterminate = true;

                try
                {
                    // Use Process.Start to run Git in a new console window for interactive input
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"fetch {remote}",
                        WorkingDirectory = repoPath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    AnsiConsole.MarkupLine($"[blue]🔄 Opening Git console for interactive authentication...[/]");
                    AnsiConsole.MarkupLine($"[dim]A new console window will open. Please authenticate there and close it when done.[/]");
                    
                    var process = System.Diagnostics.Process.Start(processInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync(cancellationToken);
                        
                        if (process.ExitCode != 0)
                        {
                            AnsiConsole.MarkupLine($"[yellow]⚠️  Git fetch completed with exit code: {process.ExitCode}[/]");
                            AnsiConsole.MarkupLine($"[blue]💡 If authentication failed, please try again.[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[green]✅ Git fetch completed successfully![/]");
                        }
                    }

                    // Note: Exit code handling is now done above in the process.WaitForExitAsync section

                    task.Value = 100;
                }
                catch (Exception ex) when (ex.Message.Contains("authentication") || ex.Message.Contains("password"))
                {
                    AnsiConsole.MarkupLine($"[red]❌ Authentication required for remote '{remote}'[/]");
                    AnsiConsole.MarkupLine($"[blue]💡 Please run 'git fetch {remote}' manually to authenticate, then try again.[/]");
                    throw;
                }
            });
    }

    public async Task<List<CommitInfo>> GetOutstandingCommitsAsync(string source, string target, CancellationToken ct = default)
    {
        var result = await _gitCommand
            .WithArguments($"log {target}..{source} --oneline --format=\"%H|%h|%s|%an|%ad\" --date=iso")
            .ExecuteBufferedAsync(ct);

        return CommitParser.ParseCommitOutput(result.StandardOutput);
    }

    public async Task<CherryPickAnalysis> GetCherryPickAnalysisAsync(string source, string target, CancellationToken ct = default)
    {
        var result = await _gitCommand
            .WithArguments($"cherry {target} {source}")
            .ExecuteBufferedAsync(ct);

        var analysis = new CherryPickAnalysis();
        var newShas = new List<string>();
        var appliedShas = new List<string>();

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("+ "))
                newShas.Add(line[2..]);

            if (line.StartsWith("- "))
                appliedShas.Add(line[2..]);
        }

        if (newShas.Count > 0)
        {
            analysis.NewCommits = await GetCommitDetailsAsync(newShas, ct);
        }

        if (appliedShas.Count > 0)
        {
            analysis.AlreadyAppliedCommits = await GetCommitDetailsAsync(appliedShas, ct);
        }

        return analysis;
    }

    public async Task<List<CommitInfo>> GetCommitDetailsAsync(List<string> shas, CancellationToken ct = default)
    {
        var shaList = string.Join(" ", shas);
        var result = await _gitCommand
            .WithArguments($"log --no-walk --format=\"%H|%h|%s|%an|%ad\" --date=iso {shaList}")
            .ExecuteBufferedAsync(ct);

        return CommitParser.ParseCommitOutput(result.StandardOutput);
    }

    public async Task<bool> CheckContentDifferencesAsync(string source, string target, CancellationToken ct = default)
    {
        var result = await _gitCommand
            .WithArguments($"diff --quiet {target}..{source}")
            .ExecuteBufferedAsync(ct);

        return result.ExitCode != 0;
    }
}
