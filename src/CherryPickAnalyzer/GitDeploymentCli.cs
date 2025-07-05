using System;
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
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new ArgumentException("Repository path cannot be null or empty", nameof(repoPath));

        try
        {
            _repoPath = Path.GetFullPath(repoPath);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid repository path: {repoPath}", nameof(repoPath), ex);
        }

        if (!Repository.IsValid(_repoPath))
        {
            throw new InvalidOperationException($"'{_repoPath}' is not a valid git repository");
        }

        try
        {
            _repo = new Repository(_repoPath);
            _gitExecutor = new GitCommandExecutor(_repoPath);
            _branchValidator = new BranchValidator(_repo);
            _repoInfoDisplay = new RepositoryInfoDisplay(_repoPath, GetRepositoryStatus());
            _analysisDisplay = new AnalysisDisplay();
        }
        catch (Exception ex)
        {
            _repo?.Dispose();
            throw new InvalidOperationException($"Failed to initialize Git repository at '{_repoPath}': {ex.Message}", ex);
        }
    }

    public async Task<int> AnalyzeAsync(AnalyzeOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        // Validate options
        ValidateAnalyzeOptions(options);

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
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        // Validate options
        ValidateCherryPickOptions(options);

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
                var diffTask = ctx.AddTask("[yellow]Content analysis[/]");

                commitTask.StartTask();
                analysis.OutstandingCommits = await _gitExecutor.GetOutstandingCommitsAsync(sourceBranch, targetBranch, cancellationToken);
                commitTask.Value = 100;

                cherryTask.StartTask();
                analysis.CherryPickAnalysis = await _gitExecutor.GetCherryPickAnalysisAsync(sourceBranch, targetBranch, cancellationToken);
                cherryTask.Value = 100;

                diffTask.StartTask();
                analysis.HasContentDifferences = await _gitExecutor.CheckContentDifferencesAsync(sourceBranch, targetBranch, cancellationToken);

                if (analysis.HasContentDifferences)
                {
                    analysis.ContentAnalysis = GetDetailedContentAnalysis(sourceBranch, targetBranch);
                }
                diffTask.Value = 100;
            });

        return analysis;
    }

    private ContentAnalysis GetDetailedContentAnalysis(string sourceBranch, string targetBranch)
    {
        var source = _branchValidator.GetBranch(sourceBranch);
        var target = _branchValidator.GetBranch(targetBranch);
        var patch = _repo.Diff.Compare<Patch>(target.Tip.Tree, source.Tip.Tree);

        var analysis = new ContentAnalysis { ChangedFiles = new List<FileChange>() };
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

    private static void ValidateAnalyzeOptions(AnalyzeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceBranch))
            throw new ArgumentException("Source branch cannot be null or empty", nameof(options.SourceBranch));

        if (string.IsNullOrWhiteSpace(options.TargetBranch))
            throw new ArgumentException("Target branch cannot be null or empty", nameof(options.TargetBranch));

        if (string.IsNullOrWhiteSpace(options.Remote))
            throw new ArgumentException("Remote cannot be null or empty", nameof(options.Remote));

        if (options.TimeoutSeconds <= 0)
            throw new ArgumentException("Timeout must be greater than 0", nameof(options.TimeoutSeconds));

        if (options.TimeoutSeconds > 3600) // 1 hour max
            throw new ArgumentException("Timeout cannot exceed 1 hour", nameof(options.TimeoutSeconds));

        var validFormats = new[] { "table", "json", "markdown" };
        if (!validFormats.Contains(options.Format?.ToLower()))
            throw new ArgumentException($"Invalid format. Valid formats are: {string.Join(", ", validFormats)}", nameof(options.Format));
    }

    private static void ValidateCherryPickOptions(CherryPickOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceBranch))
            throw new ArgumentException("Source branch cannot be null or empty", nameof(options.SourceBranch));

        if (string.IsNullOrWhiteSpace(options.TargetBranch))
            throw new ArgumentException("Target branch cannot be null or empty", nameof(options.TargetBranch));

        if (string.Equals(options.SourceBranch, options.TargetBranch, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and target branches cannot be the same");
    }

    public void Dispose()
    {
        _repo.Dispose();

        GC.SuppressFinalize(this);
    }
}
