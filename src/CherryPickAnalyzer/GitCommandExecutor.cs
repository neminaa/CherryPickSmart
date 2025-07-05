using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CliWrap;
using CliWrap.Buffered;
using GitCherryHelper.Models;
using Spectre.Console;

namespace GitCherryHelper;

public class GitCommandExecutor
{
    private readonly Command _gitCommand;

    // Regex patterns for validation
    private static readonly Regex BranchNameRegex = new(@"^[a-zA-Z0-9/_.-]+$", RegexOptions.Compiled);
    private static readonly Regex RemoteNameRegex = new(@"^[a-zA-Z0-9/_.-]+$", RegexOptions.Compiled);
    private static readonly Regex ShaRegex = new(@"^[a-fA-F0-9]{5,40}$", RegexOptions.Compiled);

    public GitCommandExecutor(string repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new ArgumentException("Repository path cannot be null or empty", nameof(repoPath));

        _gitCommand = Cli.Wrap("git")
            .WithWorkingDirectory(repoPath)
            .WithValidation(CommandResultValidation.None);
    }

    public async Task FetchWithProgressAsync(string remote, CancellationToken cancellationToken)
    {
        ValidateRemoteName(remote);

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[green]Fetching from {remote}[/]");
                task.IsIndeterminate = true;

                var result = await _gitCommand
                    .WithArguments(args => args
                        .Add("fetch")
                        .Add(remote))
                    .ExecuteBufferedAsync(cancellationToken);

                if (result.ExitCode != 0)
                {
                    throw new GitCommandException($"Failed to fetch from remote '{remote}': {result.StandardError}");
                }

                task.Value = 100;
            });
    }

    public async Task<List<CommitInfo>> GetOutstandingCommitsAsync(string source, string target, CancellationToken ct = default)
    {
        ValidateBranchName(source, nameof(source));
        ValidateBranchName(target, nameof(target));

        var result = await _gitCommand
            .WithArguments(args => args
                .Add("log")
                .Add($"{target}..{source}")
                .Add("--oneline")
                .Add("--format=%H|%h|%s|%an|%ad")
                .Add("--date=iso"))
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
        {
            throw new GitCommandException($"Failed to get outstanding commits from '{source}' to '{target}': {result.StandardError}");
        }

        return CommitParser.ParseCommitOutput(result.StandardOutput);
    }

    public async Task<CherryPickAnalysis> GetCherryPickAnalysisAsync(string source, string target, CancellationToken ct = default)
    {
        ValidateBranchName(source, nameof(source));
        ValidateBranchName(target, nameof(target));

        var result = await _gitCommand
            .WithArguments(args => args
                .Add("cherry")
                .Add(target)
                .Add(source))
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
        {
            throw new GitCommandException($"Failed to perform cherry-pick analysis for '{source}' and '{target}': {result.StandardError}");
        }

        var analysis = new CherryPickAnalysis();
        var newShas = new List<string>();
        var appliedShas = new List<string>();

        foreach (var line in result.StandardOutput.Split('\n', System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("+ ") && line.Length > 2)
            {
                var sha = line[2..].Trim();
                if (ValidateSha(sha))
                    newShas.Add(sha);
            }

            if (line.StartsWith("- ") && line.Length > 2)
            {
                var sha = line[2..].Trim();
                if (ValidateSha(sha))
                    appliedShas.Add(sha);
            }
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
        if (shas == null || shas.Count == 0)
            return new List<CommitInfo>();

        // Validate all SHAs before processing
        foreach (var sha in shas)
        {
            if (!ValidateSha(sha))
                throw new ArgumentException($"Invalid SHA format: {sha}");
        }

        var args = new List<string> { "log", "--no-walk", "--format=%H|%h|%s|%an|%ad", "--date=iso" };
        args.AddRange(shas);

        var result = await _gitCommand
            .WithArguments(args)
            .ExecuteBufferedAsync(ct);

        if (result.ExitCode != 0)
        {
            throw new GitCommandException($"Failed to get commit details: {result.StandardError}");
        }

        return CommitParser.ParseCommitOutput(result.StandardOutput);
    }

    public async Task<bool> CheckContentDifferencesAsync(string source, string target, CancellationToken ct = default)
    {
        ValidateBranchName(source, nameof(source));
        ValidateBranchName(target, nameof(target));

        var result = await _gitCommand
            .WithArguments(args => args
                .Add("diff")
                .Add("--quiet")
                .Add($"{target}..{source}"))
            .ExecuteBufferedAsync(ct);

        // Note: diff --quiet returns 1 if there are differences, 0 if no differences
        // We don't throw an exception here because exit code 1 is expected when there are differences
        return result.ExitCode != 0;
    }

    public async Task<int> ExecuteCherryPickCommandsAsync(List<string> commands)
    {
        if (commands == null || commands.Count == 0)
            return 0;

        var confirm = await AnsiConsole.ConfirmAsync("Execute these commands?");
        if (!confirm) return 0;

        foreach (var command in commands)
        {
            AnsiConsole.MarkupLine($"[dim]Executing:[/] {command}");

            // Parse and validate the command safely
            var (gitArgs, isValid) = ParseGitCommand(command);
            if (!isValid)
            {
                AnsiConsole.MarkupLine($"[red]❌ Invalid command format:[/] {command}");
                return 1;
            }

            var result = await _gitCommand
                .WithArguments(gitArgs)
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

    private static void ValidateBranchName(string branchName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException($"Branch name cannot be null or empty", parameterName);

        if (!BranchNameRegex.IsMatch(branchName))
            throw new ArgumentException($"Invalid branch name format: {branchName}", parameterName);

        // Additional security checks
        if (branchName.Contains("..") || branchName.Contains("$") || branchName.Contains("`") || 
            branchName.Contains(";") || branchName.Contains("&") || branchName.Contains("|"))
            throw new ArgumentException($"Branch name contains invalid characters: {branchName}", parameterName);
    }

    private static void ValidateRemoteName(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName))
            throw new ArgumentException("Remote name cannot be null or empty", nameof(remoteName));

        if (!RemoteNameRegex.IsMatch(remoteName))
            throw new ArgumentException($"Invalid remote name format: {remoteName}");

        // Additional security checks
        if (remoteName.Contains("$") || remoteName.Contains("`") || remoteName.Contains(";") || 
            remoteName.Contains("&") || remoteName.Contains("|"))
            throw new ArgumentException($"Remote name contains invalid characters: {remoteName}");
    }

    private static bool ValidateSha(string sha)
    {
        return !string.IsNullOrWhiteSpace(sha) && ShaRegex.IsMatch(sha);
    }

    private static (List<string> args, bool isValid) ParseGitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (new List<string>(), false);

        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // Command must start with 'git'
        if (parts.Length < 2 || parts[0] != "git")
            return (new List<string>(), false);

        var args = parts.Skip(1).ToList();

        // Only allow specific safe Git commands
        var allowedCommands = new[] { "checkout", "cherry-pick", "status", "log", "diff" };
        if (!allowedCommands.Contains(args[0]))
            return (new List<string>(), false);

        // Additional validation for specific commands
        if (args[0] == "cherry-pick" && args.Count >= 2)
        {
            // Validate SHA for cherry-pick
            if (!ValidateSha(args[1]))
                return (new List<string>(), false);
        }
        else if (args[0] == "checkout" && args.Count >= 2)
        {
            // Validate branch name for checkout
            try
            {
                ValidateBranchName(args[1], "branch");
            }
            catch
            {
                return (new List<string>(), false);
            }
        }

        return (args, true);
    }
}
