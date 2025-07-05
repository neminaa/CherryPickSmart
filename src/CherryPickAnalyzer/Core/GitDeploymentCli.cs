using CherryPickAnalyzer.Display;
using CherryPickAnalyzer.Helpers;
using CherryPickAnalyzer.Models;
using CherryPickAnalyzer.Options;
using LibGit2Sharp;
using Spectre.Console;
using CherryPickOptions = CherryPickAnalyzer.Options.CherryPickOptions;
using RepositoryStatus = CherryPickAnalyzer.Models.RepositoryStatus;

namespace CherryPickAnalyzer.Core;

public class GitDeploymentCli : IDisposable
{
    private readonly Repository _repo;
    private readonly GitCommandExecutor _gitExecutor;
    private readonly BranchValidator _branchValidator;
    private readonly RepositoryInfoDisplay _repoInfoDisplay;
    private readonly AnalysisDisplay _analysisDisplay;

    public GitDeploymentCli(string repoPath)
    {
        var repoPath1 = Path.GetFullPath(repoPath);

        if (!Repository.IsValid(repoPath1))
        {
            throw new InvalidOperationException($"'{repoPath1}' is not a valid git repository");
        }

        _repo = new Repository(repoPath1);
        _gitExecutor = new GitCommandExecutor(repoPath1);
        _branchValidator = new BranchValidator(_repo);
        _repoInfoDisplay = new RepositoryInfoDisplay(repoPath1, GetRepositoryStatus());
        _analysisDisplay = new AnalysisDisplay();
    }

    #region Public API
    public async Task<int> AnalyzeAsync(AnalyzeOptions options)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));

        // Default excludes
        var defaultExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "packages.lock.json",
            "package-lock.json",
            "yarn.lock",
            "pnpm-lock.yaml",
            "kiota-lock.json",
            "v1.json",
        };
        var excludeFiles = options.ExcludeFiles.Any() ? new HashSet<string>(options.ExcludeFiles, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(defaultExcludes, StringComparer.OrdinalIgnoreCase);

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

            var analysis = await AnalyzeWithProgressAsync(options.SourceBranch, options.TargetBranch, options.MergeHighlightMode, excludeFiles, options.ShowAllCommits, cts.Token);

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

            // Default excludes
            var defaultExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "packages.lock.json",
                "package-lock.json",
                "yarn.lock",
                "pnpm-lock.yaml"
            };
            var excludeFiles = new HashSet<string>(defaultExcludes, StringComparer.OrdinalIgnoreCase);

            var analysis = await AnalyzeWithProgressAsync(options.SourceBranch, options.TargetBranch, "ancestry", excludeFiles, true, CancellationToken.None);

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

    public void Dispose()
    {
        _repo.Dispose();

        GC.SuppressFinalize(this);
    }
    #endregion

    #region Progress Logic
    private async Task<DeploymentAnalysis> AnalyzeWithProgressAsync(
        string sourceBranch,
        string targetBranch,
        string mergeHighlightMode,
        HashSet<string> excludeFiles,
        bool showAllCommits,
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
            analysis.ContentAnalysis = await GetDetailedContentAnalysisAsync(
                sourceBranch, targetBranch, analysis.OutstandingCommits, analysis.CherryPickAnalysis.NewCommits, mergeHighlightMode, excludeFiles, showAllCommits, cancellationToken);
        }

        return analysis;
    }

    private async Task<ContentAnalysis> GetDetailedContentAnalysisAsync(
        string sourceBranch, 
        string targetBranch,
        List<CommitInfo> outstandingCommits,
        List<CommitInfo> newCherryPickCommits,
        string mergeHighlightMode,
        HashSet<string> excludeFiles,
        bool showAllCommits,
        CancellationToken cancellationToken = default)
    {
        var source = _branchValidator.GetBranch(sourceBranch);
        var target = _branchValidator.GetBranch(targetBranch);
        var patch = _repo.Diff.Compare<Patch>(target.Tip.Tree, source.Tip.Tree);
        
        var analysis = new ContentAnalysis { ChangedFiles = [] };
        int totalAdded = 0, totalDeleted = 0;

        // Build set of files touched by new cherry-pick commits
        var cherryPickFiles = new HashSet<string>();
        var cherryPickCommitShas = new HashSet<string>(newCherryPickCommits.Select(c => c.Sha));
        foreach (var commitInfo in newCherryPickCommits)
        {
            var commit = _repo.Lookup<Commit>(commitInfo.Sha);
            if (commit == null) continue;
            var parent = commit.Parents.FirstOrDefault();
            if (parent == null) continue;
            var commitPatch = _repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
            foreach (var entry in commitPatch)
            {
                cherryPickFiles.Add(entry.Path);
            }
        }

        // Precompute file-to-commits map for efficiency with progress display
        var fileToCommits = new Dictionary<string, List<CommitInfo>>();
        await AnsiConsole.Progress()
            .StartAsync(ctx =>
            {
                var commitTask = ctx.AddTask("[blue]Analyzing commits...[/]");
                var totalCommits = outstandingCommits.Count;
                var processed = 0;
                
                foreach (var commitInfo in outstandingCommits)
                {
                    var commit = _repo.Lookup<Commit>(commitInfo.Sha);
                    if (commit == null) { processed++; commitTask.Value = processed * 100.0 / totalCommits; continue; }
                    var parent = commit.Parents.FirstOrDefault();
                    if (parent == null) { processed++; commitTask.Value = processed * 100.0 / totalCommits; continue; }
                    var commitPatch = _repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
                    foreach (var entry in commitPatch)
                    {
                        if (!fileToCommits.TryGetValue(entry.Path, out var list))
                        {
                            list = [];
                            fileToCommits[entry.Path] = list;
                        }
                        list.Add(commitInfo);
                        // Show live progress for each file being mapped
                        var shortPath = entry.Path.Length > 50
                            ? "..." + entry.Path[^47..]
                            : entry.Path.PadRight(50, '.');
                        
                        commitTask.Description = $"[blue]Analyzing commits...:[/] {Markup.Escape(shortPath)} ([dim]{Markup.Escape(commitInfo.ShortSha)}[/] {Markup.Escape(commitInfo.Author)})";
                        ctx.Refresh();
                    }
                    processed++;
                    commitTask.Value = processed * 100.0 / totalCommits;
                }
                
                commitTask.Description($"[green]Analyzed {processed} commits...:[/]");
                return Task.CompletedTask;
            });

        // Precompute merge commit coverage
        var mergeToCherryPicks = new Dictionary<string, HashSet<string>>();
        var cherryPicksCoveredByMerge = new HashSet<string>();
        // Collect all relevant commits (from all files)
        var allRelevantCommits = new HashSet<string>();
        foreach (var c in fileToCommits.Values.SelectMany(commitList => commitList))
            allRelevantCommits.Add(c.Sha);

        // For ancestry mode: collect merge SHAs in first-parent history of source branch
        var firstParentMerges = new HashSet<string>();
        if (mergeHighlightMode == "ancestry")
        {
            var branch = _repo.Branches[sourceBranch];
            if (branch != null && branch.Tip != null)
            {
                var commit = branch.Tip;
                while (commit != null)
                {
                    if (commit.Parents.Count() > 1)
                        firstParentMerges.Add(commit.Sha);
                    commit = commit.Parents.FirstOrDefault();
                }
            }
        }

        foreach (var sha in allRelevantCommits)
        {
            var commit = _repo.Lookup<Commit>(sha);
            if (commit == null) continue;
            if (commit.Parents.Count() > 1)
            {
                // Only consider merge commits in first-parent history for ancestry mode
                var foundCherryPicks = new HashSet<string>();
                if (mergeHighlightMode == "message")
                {
                    // Highlight if commit message contains "into 'sourceBranch'"
                    if (commit.Message.Contains($"into '{sourceBranch}'", StringComparison.OrdinalIgnoreCase))
                    {
                        // For message mode, just check if any cherry-pick commit is in ancestry (direct parents)
                        foreach (var parent in commit.Parents)
                        {
                            if (cherryPickCommitShas.Contains(parent.Sha))
                            {
                                foundCherryPicks.Add(parent.Sha);
                                cherryPicksCoveredByMerge.Add(parent.Sha);
                            }
                        }
                    }
                }
                else // ancestry (default)
                {
                    if (!firstParentMerges.Contains(commit.Sha))
                    {
                        continue;
                    }
                    
                    var queue = new Queue<Commit>(commit.Parents);
                    var depth = 0;
                    while (queue.Count > 0 && depth < 100)
                    {
                        var ancestor = queue.Dequeue();
                        if (cherryPickCommitShas.Contains(ancestor.Sha))
                        {
                            foundCherryPicks.Add(ancestor.Sha);
                            cherryPicksCoveredByMerge.Add(ancestor.Sha);
                        }
                        foreach (var p in ancestor.Parents)
                            queue.Enqueue(p);
                        depth++;
                    }
                }
                if (foundCherryPicks.Count > 0)
                    mergeToCherryPicks[commit.Sha] = foundCherryPicks;
            }
        }

        // Filter mergeToCherryPicks to only show maximal (superset) MRs
        var maximalMerges = new HashSet<string>();
        var mergeList = mergeToCherryPicks.ToList();
        for (var i = 0; i < mergeList.Count; i++)
        {
            var (shaI, setI) = mergeList[i];
            var isSubsumed = false;
            for (var j = 0; j < mergeList.Count; j++)
            {
                if (i == j) continue;
                var (_, setJ) = mergeList[j];
                if (!setI.All(setJ.Contains)) continue;
                isSubsumed = true;
                break;
            }
            if (!isSubsumed)
                maximalMerges.Add(shaI);
        }

        // Create tree for hierarchical file change display
        var tree = new Spectre.Console.Tree("📁 File Changes")
            .Style(Style.Parse("blue"));

        await AnsiConsole.Live(tree)
            .AutoClear(true)
            .StartAsync(async ctx =>
            {
                foreach (var change in patch)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    var fileName = Path.GetFileName(change.Path);
                    if (excludeFiles.Contains(fileName))
                        continue;

                    var fileChange = new FileChange
                    {
                        NewPath = change.Path,
                        Status = change.Status.ToString(),
                        LinesAdded = change.LinesAdded,
                        LinesDeleted = change.LinesDeleted,
                        Commits = fileToCommits.TryGetValue(change.Path, out var commits) ? commits : []
                    };

                    analysis.ChangedFiles.Add(fileChange);
                    totalAdded += change.LinesAdded;
                    totalDeleted += change.LinesDeleted;

                    // Create file node with status icon and change details
                    var statusIcon = fileChange.Status switch
                    {
                        "Added" => "[green]➕[/]",
                        "Modified" => "[yellow]🔄[/]",
                        "Deleted" => "[red]➖[/]",
                        "Renamed" => "[blue]📝[/]",
                        _ => "[dim]❓[/]"
                    };

                    var commitCountBadge = fileChange.Commits.Count > 1 ? $" [grey]{fileChange.Commits.Count} commits[/]" : string.Empty;
                    var changeText = $"[green]+{fileChange.LinesAdded}[/] [red]-{fileChange.LinesDeleted}[/]";
                    var fileNode = new TreeNode(new Markup($"{statusIcon} {Markup.Escape(fileName)}{commitCountBadge} {changeText}"));

                    // Add commit sub-nodes
                    foreach (var commit in fileChange.Commits)
                    {
                        var isMergeWithCherry = mergeToCherryPicks.ContainsKey(commit.Sha) && maximalMerges.Contains(commit.Sha);
                        var isCherryPickCommit = cherryPickCommitShas.Contains(commit.Sha);
                        if (!showAllCommits)
                        {
                            if (!isMergeWithCherry && !isCherryPickCommit)
                                continue;
                        }
                        var isCoveredByMerge = cherryPicksCoveredByMerge.Contains(commit.Sha);
                        var isRecent = (DateTimeOffset.UtcNow - commit.Date).TotalDays <= 7;
                        string commitColor;
                        var cherryIcon = "";
                        if (isMergeWithCherry)
                        {
                            commitColor = "bold magenta";
                            cherryIcon = "🍒 ";
                            
                        }
                        else if (isCherryPickCommit && !isCoveredByMerge)
                        {
                            commitColor = "bold magenta";
                            cherryIcon = "🍒 ";
                            if (isCoveredByMerge)
                            {
                                commitColor = "dim cyan";
                                cherryIcon = "";

                            }
                        }
                        else if (isRecent)
                        {
                            commitColor = "bold yellow";
                        }
                        else
                        {
                            commitColor = "dim";
                        }
                        var commitText = $"[{commitColor}]{cherryIcon}{Markup.Escape(commit.ShortSha)}[/] [blue]{Markup.Escape(commit.Author)}[/]: {Markup.Escape(commit.Message)} [grey]({commit.Date:yyyy-MM-dd})[/]";
                        
                        
                        var subNode = fileNode.AddNode(new TreeNode(new Markup(commitText)));

                        if (isMergeWithCherry)
                        {
                            // Add sub-nodes for each included cherry-pick commit
                            foreach (var cherrySha in mergeToCherryPicks[commit.Sha])
                            {
                                var cherryCommit = fileChange.Commits.FirstOrDefault(c => c.Sha == cherrySha);
                                if (cherryCommit == null) continue;
                                var cherryText =
                                    $"[dim cyan] {Markup.Escape(cherryCommit.ShortSha)}[/] [blue]{Markup.Escape(cherryCommit.Author)}[/]: {Markup.Escape(cherryCommit.Message)} [grey]({cherryCommit.Date:yyyy-MM-dd})[/]";
                                subNode.AddNode(new TreeNode(new Markup(cherryText)));
                            }
                        }

                    }

                    // Add to tree based on file path structure (for now, just add to root)
                    FileTreeHelper.AddFileToTree(tree, fileChange.NewPath, fileNode);

                    ctx.Refresh();
                    await Task.Delay(10, cancellationToken); // Small delay for visual effect
                }
            });
        // Print the tree again so it remains visible after the live context
        AnsiConsole.Write(tree);

        analysis.Stats = new DiffStats
        {
            FilesChanged = patch.Count(),
            LinesAdded = totalAdded,
            LinesDeleted = totalDeleted
        };

        return analysis;
    }
    #endregion

    #region Private Helpers
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
    #endregion
}
