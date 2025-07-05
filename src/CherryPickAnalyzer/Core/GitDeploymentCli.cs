using System.Text.Json;
using CliWrap;
using CliWrap.Buffered;
using GitCherryHelper.Models;
using GitCherryHelper.Options;
using GitCherryHelper.Display;
using GitCherryHelper.Helpers;
using LibGit2Sharp;
using Spectre.Console;
using CherryPickOptions = GitCherryHelper.Options.CherryPickOptions;
using RepositoryStatus = GitCherryHelper.Models.RepositoryStatus;
using Tree = LibGit2Sharp.Tree;

namespace GitCherryHelper.Core;

public class GitDeploymentCli : IDisposable
{
    private readonly Repository _repo;
    private readonly string _repoPath;
    private readonly GitCommandExecutor _gitExecutor;
    private readonly BranchValidator _branchValidator;
    private readonly RepositoryInfoDisplay _repoInfoDisplay;
    private readonly AnalysisDisplay _analysisDisplay;

    private static readonly System.Text.Json.JsonSerializerOptions CachedJsonSerializerOptions = new()
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
        _gitExecutor = new GitCommandExecutor(_repoPath);
        _branchValidator = new BranchValidator(_repo);
        _repoInfoDisplay = new RepositoryInfoDisplay(_repoPath, GetRepositoryStatus());
        _analysisDisplay = new AnalysisDisplay();
    }

    public async Task<int> AnalyzeAsync(AnalyzeOptions options)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            AnsiConsole.Write(new FigletText("TSP GIT Analyzer")
                .LeftJustified()
                .Color(Color.Blue));

            _repoInfoDisplay.DisplayRepositoryInfo(_repo.Head.FriendlyName, _repo.Network.Remotes.Select(r => r.Name));
            _repoInfoDisplay.DisplayRepositoryStatus();

            if (!options.NoFetch)
            {
                await _gitExecutor.FetchWithProgressAsync(options.Remote, cts.Token);
            }

            _branchValidator.ValidateBranches(options.SourceBranch, options.TargetBranch);

            var analysis = await AnalyzeWithProgressAsync(options.SourceBranch, options.TargetBranch, cts.Token);

            switch (options.Format.ToLower())
            {
                case "table":
                    _analysisDisplay.DisplayAnalysisAsTable(analysis, options.SourceBranch, options.TargetBranch);
                    var commands = CherryPickHelper.GenerateCherryPickCommands(options.TargetBranch,
                        analysis.CherryPickAnalysis.AlreadyAppliedCommits);
                    CherryPickHelper.DisplayCherryPickCommands(commands);
                    break;
                case "json":
                    _analysisDisplay.DisplayAnalysisAsJson(analysis);
                    break;
                case "markdown":
                    _analysisDisplay.DisplayAnalysisAsMarkdown(analysis, options.SourceBranch, options.TargetBranch);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown format: {options.Format}[/]");
                    return 1;
            }

            _analysisDisplay.DisplaySuggestions(analysis, options.TargetBranch);

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

            _branchValidator.ValidateBranches(options.SourceBranch, options.TargetBranch);

            var analysis = await AnalyzeWithProgressAsync(options.SourceBranch, options.TargetBranch);

            if (analysis.CherryPickAnalysis.NewCommits.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✅ No commits to cherry-pick - branches are in sync![/]");
                return 0;
            }

            var commitsToApply = analysis.CherryPickAnalysis.NewCommits;

            if (options.Interactive)
            {
                commitsToApply = CherryPickHelper.SelectCommitsInteractively(analysis.CherryPickAnalysis.NewCommits);
            }

            if (commitsToApply.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No commits selected[/]");
                return 0;
            }

            var commands = CherryPickHelper.GenerateCherryPickCommands(options.TargetBranch, commitsToApply);

            CherryPickHelper.DisplayCherryPickCommands(commands);

            if (options.Execute)
            {
                return await _gitExecutor.ExecuteCherryPickCommandsAsync(commands);
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
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

                commitTask.StartTask();
                analysis.OutstandingCommits = await _gitExecutor.GetOutstandingCommitsAsync(sourceBranch, targetBranch, cancellationToken);
                commitTask.Value = 100;

                cherryTask.StartTask();
                analysis.CherryPickAnalysis = await _gitExecutor.GetCherryPickAnalysisAsync(sourceBranch, targetBranch, cancellationToken);
                cherryTask.Value = 100;
            });

        // Check for content differences outside of progress bar
        analysis.HasContentDifferences = await _gitExecutor.CheckContentDifferencesAsync(sourceBranch, targetBranch, cancellationToken);

        if (analysis.HasContentDifferences)
        {
            analysis.ContentAnalysis = await GetDetailedContentAnalysisAsync(sourceBranch, targetBranch, cancellationToken);
        }

        return analysis;
    }

    private async Task<ContentAnalysis> GetDetailedContentAnalysisAsync(
        string sourceBranch, 
        string targetBranch,
        CancellationToken cancellationToken = default)
    {
        var source = _branchValidator.GetBranch(sourceBranch);
        var target = _branchValidator.GetBranch(targetBranch);
        var patch = _repo.Diff.Compare<Patch>(target.Tip.Tree, source.Tip.Tree);
        
        var analysis = new ContentAnalysis { ChangedFiles = new List<FileChange>() };
        int totalAdded = 0, totalDeleted = 0;

        // Get all commits between branches to map files to commits
        var commits = await _gitExecutor.GetOutstandingCommitsAsync(sourceBranch, targetBranch, cancellationToken);
        var fileToCommitMap = await BuildFileToCommitMapAsync(commits, sourceBranch, targetBranch, cancellationToken);

        // Create live table for real-time file change display with commit info
        var liveTable = new Table()
            .AddColumn("Status")
            .AddColumn("File")
            .AddColumn("Changes")
            .AddColumn("Commit")
            .AddColumn("Author")
            .BorderColor(Color.Yellow);

        await AnsiConsole.Live(liveTable)
            .StartAsync(async ctx =>
            {
                foreach (var change in patch)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    // Find the commit that introduced this change
                    var commitInfo = fileToCommitMap.TryGetValue(change.Path, out var commit) ? commit : null;

                    var fileChange = new FileChange
                    {
                        NewPath = change.Path,
                        Status = change.Status.ToString(),
                        LinesAdded = change.LinesAdded,
                        LinesDeleted = change.LinesDeleted,
                        CommitSha = commitInfo?.Sha ?? "",
                        CommitMessage = commitInfo?.Message ?? "",
                        Author = commitInfo?.Author ?? ""
                    };

                    analysis.ChangedFiles.Add(fileChange);
                    totalAdded += change.LinesAdded;
                    totalDeleted += change.LinesDeleted;

                    // Add row to live table with status icon and commit info
                    var statusIcon = fileChange.Status switch
                    {
                        "Added" => "[green]➕[/]",
                        "Modified" => "[yellow]🔄[/]",
                        "Deleted" => "[red]➖[/]",
                        "Renamed" => "[blue]📝[/]",
                        _ => "[dim]❓[/]"
                    };

                    var commitDisplay = commitInfo != null 
                        ? $"[dim]{commitInfo.ShortSha}[/] {(commitInfo.Message.Length > 30 ? string.Concat(commitInfo.Message.AsSpan(0, 27), "...") : commitInfo.Message)}"
                        : "[dim]Unknown[/]";

                    liveTable.AddRow(
                        statusIcon,
                        fileChange.NewPath,
                        $"[green]+{fileChange.LinesAdded}[/] [red]-{fileChange.LinesDeleted}[/]",
                        commitDisplay,
                        commitInfo?.Author ?? "[dim]Unknown[/]"
                    );

                    ctx.Refresh();
                    await Task.Delay(10, cancellationToken); // Small delay for visual effect
                }
            });

        analysis.Stats = new DiffStats
        {
            FilesChanged = patch.Count(),
            LinesAdded = totalAdded,
            LinesDeleted = totalDeleted
        };

        return analysis;
    }

    private async Task<Dictionary<string, CommitInfo>> BuildFileToCommitMapAsync(
        List<CommitInfo> commits, 
        string sourceBranch, 
        string targetBranch, 
        CancellationToken cancellationToken)
    {
        var fileToCommitMap = new Dictionary<string, CommitInfo>();

        foreach (var commit in commits.Take(50)) // Limit to first 50 commits for performance
        {
            try
            {
                // Get files changed in this commit
                var result = await _gitExecutor.GetGitCommand()
                    .WithArguments($"show --name-only --format=\"\" {commit.Sha}")
                    .ExecuteBufferedAsync(cancellationToken);

                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
                {
                    var files = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var file in files)
                    {
                        if (!fileToCommitMap.ContainsKey(file))
                        {
                            fileToCommitMap[file] = commit;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Continue if we can't get files for this commit
            }
        }

        return fileToCommitMap;
    }

    public void Dispose()
    {
        _repo.Dispose();

        GC.SuppressFinalize(this);
    }
}
