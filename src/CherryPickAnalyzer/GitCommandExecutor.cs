using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using CliWrap;
using CliWrap.Buffered;
using GitCherryHelper.Models;
using Spectre.Console;

namespace GitCherryHelper;

public class GitCommandExecutor
{
    private readonly Command _gitCommand;

    public GitCommandExecutor(string repoPath)
    {
        _gitCommand = Cli.Wrap("git")
            .WithWorkingDirectory(repoPath)
            .WithValidation(CommandResultValidation.None);
    }

    public async Task FetchWithProgressAsync(string remote, CancellationToken cancellationToken)
    {
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Fetching from {remote}[/]");
                task.IsIndeterminate = true;

                var result = await _gitCommand
                    .WithArguments($"fetch {remote}")
                    .ExecuteBufferedAsync(cancellationToken);

                if (result.ExitCode != 0)
                {
                    throw new GitCommandException($"Failed to fetch: {result.StandardError}");
                }

                task.Value = 100;
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

        foreach (var line in result.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries))
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

    public async Task<int> ExecuteCherryPickCommandsAsync(List<string> commands)
    {
        var confirm = await AnsiConsole.ConfirmAsync("Execute these commands?");
        if (!confirm) return 0;

        foreach (var command in commands)
        {
            AnsiConsole.MarkupLine($"[dim]Executing:[/] {command}");

            var parts = command.Split(' ', 2);
            var result = await _gitCommand
                .WithArguments(parts[1])
                .ExecuteBufferedAsync();

            if (result.ExitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]❌ Command failed:[/] {result.StandardError}");
                return 1;
            }

            AnsiConsole.MarkupLine("[green]✅ Success[/]");
        }

        return 0;
    }
}
