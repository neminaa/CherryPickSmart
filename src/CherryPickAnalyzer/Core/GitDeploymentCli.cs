using CherryPickAnalyzer.Display;
using CherryPickAnalyzer.Helpers;
using CherryPickAnalyzer.Models;
using CherryPickAnalyzer.Options;
using LibGit2Sharp;
using Spectre.Console;
using CherryPickOptions = CherryPickAnalyzer.Options.CherryPickOptions;
using RepositoryStatus = CherryPickAnalyzer.Models.RepositoryStatus;
using System.IO;

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
            analysis.ContentAnalysis = await GetDetailedContentAnalysisAsync(
                sourceBranch, targetBranch, analysis.OutstandingCommits, analysis.CherryPickAnalysis.NewCommits, cancellationToken);
        }

        return analysis;
    }

    private async Task<ContentAnalysis> GetDetailedContentAnalysisAsync(
        string sourceBranch, 
        string targetBranch,
        List<CommitInfo> outstandingCommits,
        List<CommitInfo> newCherryPickCommits,
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
                        var shortPath = entry.Path.Length > 50 ? "..." + entry.Path[^47..] : entry.Path;
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
        foreach (var commitList in fileToCommits.Values)
            foreach (var c in commitList)
                allRelevantCommits.Add(c.Sha);
        foreach (var sha in allRelevantCommits)
        {
            var commit = _repo.Lookup<Commit>(sha);
            if (commit == null) continue;
            if (commit.Parents.Count() > 1) // merge commit
            {
                var foundCherryPicks = new HashSet<string>();
                var queue = new Queue<Commit>(commit.Parents);
                int depth = 0;
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
                if (foundCherryPicks.Count > 0)
                    mergeToCherryPicks[commit.Sha] = foundCherryPicks;
            }
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

                    var fileName = Path.GetFileName(fileChange.NewPath);
                    var commitCountBadge = fileChange.Commits.Count > 1 ? $" [grey]{fileChange.Commits.Count} commits[/]" : string.Empty;
                    var changeText = $"[green]+{fileChange.LinesAdded}[/] [red]-{fileChange.LinesDeleted}[/]";
                    var fileNode = new TreeNode(new Markup($"{statusIcon} {Markup.Escape(fileName)}{commitCountBadge} {changeText}"));

                    // Add commit sub-nodes
                    foreach (var commit in fileChange.Commits)
                    {
                        var isMergeWithCherry = mergeToCherryPicks.ContainsKey(commit.Sha);
                        var isCherryPickCommit = cherryPickCommitShas.Contains(commit.Sha);
                        var isCoveredByMerge = cherryPicksCoveredByMerge.Contains(commit.Sha);
                        var isRecent = (DateTimeOffset.UtcNow - commit.Date).TotalDays <= 7;
                        string commitColor;
                        string cherryIcon = "";
                        if (isMergeWithCherry)
                        {
                            commitColor = "bold magenta";
                            cherryIcon = "🍒 ";
                        }
                        else if (isCherryPickCommit && isCoveredByMerge)
                        {
                            commitColor = "dim";
                        }
                        else if (isCherryPickCommit)
                        {
                            commitColor = "bold magenta";
                            cherryIcon = "🍒 ";
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
                        fileNode.AddNode(new TreeNode(new Markup(commitText)));
                    }

                    // Add to tree based on file path structure (for now, just add to root)
                    AddFileToTree(tree, fileChange.NewPath, fileNode);

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

    private void AddFileToTree(Spectre.Console.Tree tree, string filePath, TreeNode fileNode)
    {
        var pathParts = filePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        object currentLevel = tree;
        TreeNode currentNode = null;

        // Traverse or create folder nodes
        for (var i = 0; i < pathParts.Length - 1; i++)
        {
            var part = Markup.Escape(pathParts[i]);
            var nodes = currentLevel is Spectre.Console.Tree t ? t.Nodes : ((TreeNode)currentLevel).Nodes;
            var existing = nodes.FirstOrDefault(n => n.ToString().Contains(part));
            if (existing == null)
            {
                var dirNode = new TreeNode(new Markup($"[blue]📁 {part}[/]"));
                // Collapse folders with more than 5 children by default
                //if (nodes.Count > 5) dirNode.Collapse();
                nodes.Add(dirNode);
                currentNode = dirNode;
            }
            else
            {
                currentNode = existing;
            }
            currentLevel = currentNode;
        }

        // Add the file node to the correct folder
        var fileParentNodes = currentLevel is Spectre.Console.Tree t2 ? t2.Nodes : ((TreeNode)currentLevel).Nodes;
        fileParentNodes.Add(fileNode);
    }

    public void Dispose()
    {
        _repo.Dispose();

        GC.SuppressFinalize(this);
    }
}
