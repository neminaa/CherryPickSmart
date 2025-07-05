using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using GitCherryHelper.Models;
using GitCherryHelper.Options;
using LibGit2Sharp;
using Spectre.Console;
using CherryPickOptions = GitCherryHelper.Options.CherryPickOptions;
using RepositoryStatus = GitCherryHelper.Models.RepositoryStatus;

namespace GitCherryHelper;

public class GitDeploymentCli : IDisposable
{
    private readonly Repository _repo;
    private readonly string _repoPath;
    private readonly Command _gitCommand;

    private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
    {
        WriteIndented = true
    };
    public GitDeploymentCli(string repoPath)
    {
        _repoPath = Path.GetFullPath(repoPath);

        if (!Repository.IsValid(_repoPath))
        {
            throw new InvalidOperationException($"'{_repoPath}' is not a valid git repository");
        }

        _repo = new Repository(_repoPath);
        _gitCommand = Cli.Wrap("git")
            .WithWorkingDirectory(_repoPath)
            .WithValidation(CommandResultValidation.None);
    }

    public async Task<int> AnalyzeAsync(AnalyzeOptions options)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            AnsiConsole.Write(new FigletText("TSP GIT Analyzer")
                .LeftJustified()
                .Color(Color.Blue));

            // Show repository info
            DisplayRepositoryInfo();

            // Check repository status
            var status = GetRepositoryStatus();
            DisplayRepositoryStatus(status);

            // Fetch if requested
            if (!options.NoFetch)
            {
                await FetchWithProgressAsync(options.Remote, cts.Token);
            }

            // Validate branches
            ValidateBranches(options.SourceBranch, options.TargetBranch);

            // Perform analysis with progress
            var analysis = await AnalyzeWithProgressAsync(
                options.SourceBranch,
                options.TargetBranch,
                cts.Token);

            // Display results based on format
            switch (options.Format.ToLower())
            {
                case "table":
                    DisplayAnalysisAsTable(analysis, options.SourceBranch, options.TargetBranch);
                    var commands = GenerateCherryPickCommands(options.TargetBranch,
                        analysis.CherryPickAnalysis.AlreadyAppliedCommits);

                    DisplayCherryPickCommands(commands);

                    break;
                case "json":
                    DisplayAnalysisAsJson(analysis);
                    break;
                case "markdown":
                    DisplayAnalysisAsMarkdown(analysis, options.SourceBranch, options.TargetBranch);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown format: {options.Format}[/]");
                    return 1;
            }

            // Show suggestions
            DisplaySuggestions(analysis, options.TargetBranch);

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[red]Operation timed out[/]");
            return 2;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    public async Task<int> CherryPickAsync(CherryPickOptions options)
    {
        try
        {
            AnsiConsole.Write(new FigletText("TSP GIT Analyzer")
                .LeftJustified()
                .Color(Color.Green));

            ValidateBranches(options.SourceBranch, options.TargetBranch);

            var analysis = await AnalyzeWithProgressAsync(options.SourceBranch, options.TargetBranch);

            if (analysis.CherryPickAnalysis.NewCommits.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✅ No commits to cherry-pick - branches are in sync![/]");
                return 0;
            }

            var commitsToApply = analysis.CherryPickAnalysis.NewCommits;

            if (options.Interactive)
            {
                commitsToApply = SelectCommitsInteractively(analysis.CherryPickAnalysis.NewCommits);
            }

            if (commitsToApply.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No commits selected[/]");
                return 0;
            }

            var commands = GenerateCherryPickCommands(options.TargetBranch, commitsToApply);

            DisplayCherryPickCommands(commands);

            if (options.Execute)
            {
                return await ExecuteCherryPickCommandsAsync(commands);
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private void DisplayRepositoryInfo()
    {
        var panel = new Panel(new Markup($"""
            [bold]Repository:[/] {_repoPath}
            [bold]Current Branch:[/] {_repo.Head.FriendlyName}
            [bold]Remotes:[/] {string.Join(", ", _repo.Network.Remotes.Select(r => r.Name))}
            """))
            .Header("📁 Repository Information")
            .BorderColor(Color.Blue);

        AnsiConsole.Write(panel);
    }

    private RepositoryStatus GetRepositoryStatus()
    {
        var status = _repo.RetrieveStatus();
        return new RepositoryStatus
        {
            HasUncommittedChanges = status.IsDirty,
            UntrackedFiles = [.. status.Untracked.Select(f => f.FilePath)],
            ModifiedFiles = [.. status.Modified.Select(f => f.FilePath)],
            StagedFiles = [.. status.Staged.Select(f => f.FilePath)]
        };
    }

    private static void DisplayRepositoryStatus(RepositoryStatus status)
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

    private async Task FetchWithProgressAsync(string remote, CancellationToken cancellationToken)
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

    private void ValidateBranches(string sourceBranch, string targetBranch)
    {
        var source = GetBranch(sourceBranch);
        var target = GetBranch(targetBranch);

        if (source == null)
            throw new ArgumentException($"Source branch '{sourceBranch}' not found");
        if (target == null)
            throw new ArgumentException($"Target branch '{targetBranch}' not found");
    }

    private Branch GetBranch(string branchName)
    {
        return _repo.Branches[branchName] ??
               _repo.Branches[$"origin/{branchName}"];
    }

    private async Task<DeploymentAnalysis> AnalyzeWithProgressAsync(
        string sourceBranch,
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        var analysis = new DeploymentAnalysis();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var commitTask = ctx.AddTask("[blue]Analyzing commits[/]");
                var cherryTask = ctx.AddTask("[green]Cherry-pick analysis[/]");
                var diffTask = ctx.AddTask("[yellow]Content analysis[/]");

                // Outstanding commits
                commitTask.StartTask();
                analysis.OutstandingCommits = await GetOutstandingCommitsAsync(sourceBranch, targetBranch, cancellationToken);
                commitTask.Value = 100;

                // Cherry-pick analysis
                cherryTask.StartTask();
                analysis.CherryPickAnalysis = await GetCherryPickAnalysisAsync(sourceBranch, targetBranch, cancellationToken);
                cherryTask.Value = 100;

                // Content differences
                diffTask.StartTask();
                analysis.HasContentDifferences = await CheckContentDifferencesAsync(sourceBranch, targetBranch, cancellationToken);

                if (analysis.HasContentDifferences)
                {
                    analysis.ContentAnalysis = GetDetailedContentAnalysis(sourceBranch, targetBranch);
                }
                diffTask.Value = 100;
            });

        return analysis;
    }

    private static void DisplayAnalysisAsTable(DeploymentAnalysis analysis, string sourceBranch, string targetBranch)
    {
        // Summary table
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

        // Outstanding commits details
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

        // Content differences
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

    private static void DisplaySuggestions(DeploymentAnalysis analysis, string targetBranch)
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

    private static List<CommitInfo> SelectCommitsInteractively(List<CommitInfo> commits)
    {
        var prompt = new MultiSelectionPrompt<CommitInfo>()
            .Title("Select commits to cherry-pick:")
            .PageSize(10)
            .UseConverter(commit => $"{commit.ShortSha} {commit.Message} ({commit.Author})")
            .AddChoices(commits);

        return AnsiConsole.Prompt(prompt);
    }

    private static List<string> GenerateCherryPickCommands(string targetBranch, List<CommitInfo> commits)
    {
        var commands = new List<string> { $"git checkout {targetBranch}" };
        commands.AddRange(commits.Select(c => $"git cherry-pick {c.Sha}"));
        return commands;
    }

    private static void DisplayCherryPickCommands(List<string> commands)
    {
        var table = new Table()
            .AddColumn("Step")
            .AddColumn("Command")
            .BorderColor(Color.Green);

        for (var i = 0; i < commands.Count; i++)
        {
            table.AddRow((i + 1).ToString(), $"[dim]{commands[i]}[/]");
        }

        AnsiConsole.Write(new Panel(table)
            .Header("🍒 Cherry-pick Commands")
            .BorderColor(Color.Green));
    }

    private async Task<int> ExecuteCherryPickCommandsAsync(List<string> commands)
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


    private static void DisplayAnalysisAsJson(DeploymentAnalysis analysis)
    {
        var json = JsonSerializer.Serialize(analysis, CachedJsonSerializerOptions);
        AnsiConsole.WriteLine(json);
    }

    private static void DisplayAnalysisAsMarkdown(DeploymentAnalysis analysis, string sourceBranch, string targetBranch)
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
            {string.Join("\n", GenerateCherryPickCommands(targetBranch, analysis.CherryPickAnalysis.NewCommits))}
            ```
            """;

        AnsiConsole.WriteLine(markdown);
    }

    // Git command implementations (same as before but simplified for space)
    private async Task<List<CommitInfo>> GetOutstandingCommitsAsync(string source, string target, CancellationToken ct = default)
    {
        var result = await _gitCommand
            .WithArguments($"log {target}..{source} --oneline --format=\"%H|%h|%s|%an|%ad\" --date=iso")
            .ExecuteBufferedAsync(ct);

        return ParseCommitOutput(result.StandardOutput);
    }

    private async Task<CherryPickAnalysis> GetCherryPickAnalysisAsync(string source, string target, CancellationToken ct = default)
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
            
            if(line.StartsWith("- "))
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

    private async Task<List<CommitInfo>> GetCommitDetailsAsync(List<string> shas, CancellationToken ct = default)
    {
        var shaList = string.Join(" ", shas);
        var result = await _gitCommand
            .WithArguments($"log --no-walk --format=\"%H|%h|%s|%an|%ad\" --date=iso {shaList}")
            .ExecuteBufferedAsync(ct);

        return ParseCommitOutput(result.StandardOutput);
    }

    private async Task<bool> CheckContentDifferencesAsync(string source, string target, CancellationToken ct = default)
    {
        var result = await _gitCommand
            .WithArguments($"diff --quiet {target}..{source}")
            .ExecuteBufferedAsync(ct);

        return result.ExitCode != 0;
    }

    private ContentAnalysis GetDetailedContentAnalysis(string sourceBranch, string targetBranch)
    {
        var source = GetBranch(sourceBranch);
        var target = GetBranch(targetBranch);
        var patch = _repo.Diff.Compare<Patch>(target.Tip.Tree, source.Tip.Tree);

        var analysis = new ContentAnalysis { ChangedFiles = [] };
        int totalAdded = 0, totalDeleted = 0;

        foreach (var change in patch)
        {
            analysis.ChangedFiles.Add(new FileChange
            {
                NewPath = change.Path,
                Status = change.Status.ToString(),
                LinesAdded = change.LinesAdded,
                LinesDeleted = change.LinesDeleted
            });
            totalAdded += change.LinesAdded;
            totalDeleted += change.LinesDeleted;
        }

        analysis.Stats = new DiffStats
        {
            FilesChanged = patch.Count(),
            LinesAdded = totalAdded,
            LinesDeleted = totalDeleted
        };

        return analysis;
    }

    private static List<CommitInfo> ParseCommitOutput(string output)
    {
        return [.. from line in output
                .Split('\n', 
                    StringSplitOptions.RemoveEmptyEntries)
            select line.Split('|')
            into parts
            where parts.Length >= 5
            select new CommitInfo
            {
                Sha = parts[0],
                ShortSha = parts[1],
                Message = parts[2],
                Author = parts[3],
                Date = DateTimeOffset.Parse(parts[4])
            }];
    }

    public void Dispose()
    {
        _repo.Dispose();
        
        GC.SuppressFinalize(this);
    }
}