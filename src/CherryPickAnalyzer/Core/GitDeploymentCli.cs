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
        
        var analysis = new ContentAnalysis { ChangedFiles = [] };
        const int totalAdded = 0;
        const int totalDeleted = 0;

        // Build set of cherry-pick commit SHAs for quick lookup
        var cherryPickCommitShas = new HashSet<string>(newCherryPickCommits.Select(c => c.Sha));

        // Precompute merge commit coverage using maximal/superset approach
        var mergeToCherryPicks = new Dictionary<string, HashSet<string>>();
        var cherryPicksCoveredByMerge = new HashSet<string>();
        
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

        // Find merge commits that include cherry-pick commits
        foreach (var commitInfo in outstandingCommits)
        {
            var commit = _repo.Lookup<Commit>(commitInfo.Sha);
            if (commit == null || commit.Parents.Count() <= 1) continue;

            // Only consider merge commits in first-parent history for ancestry mode
            if (mergeHighlightMode == "ancestry" && !firstParentMerges.Contains(commit.Sha))
                continue;

            var foundCherryPicks = new HashSet<string>();
            
            if (mergeHighlightMode == "message")
            {
                // Highlight if commit message contains "into 'sourceBranch'"
                if (commit.Message.Contains($"into '{sourceBranch}'", StringComparison.OrdinalIgnoreCase))
                {
                    // For message mode, just check if any cherry-pick commit is in ancestry (direct parents)
                    foreach (var parent in commit.Parents)
                    {
                        if (!cherryPickCommitShas.Contains(parent.Sha)) continue;
                        foundCherryPicks.Add(parent.Sha);
                        cherryPicksCoveredByMerge.Add(parent.Sha);
                    }
                }
            }
            else // ancestry (default)
            {
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

        // Apply maximal/superset MR approach - keep only the most comprehensive merges
        var maximalMerges = new Dictionary<string, HashSet<string>>();
        var mergeCommits = mergeToCherryPicks.Keys.ToList();
        
        for (var i = 0; i < mergeCommits.Count; i++)
        {
            var currentMerge = mergeCommits[i];
            var currentCherryPicks = mergeToCherryPicks[currentMerge];
            var isMaximal = true;
            
            // Check if this merge is superseded by a later, more comprehensive merge
            for (var j = i + 1; j < mergeCommits.Count; j++)
            {
                var laterMerge = mergeCommits[j];
                var laterCherryPicks = mergeToCherryPicks[laterMerge];
                
                // If later merge includes all cherry-picks from current merge, current is not maximal
                if (!currentCherryPicks.All(cp => laterCherryPicks.Contains(cp))) continue;
                isMaximal = false;
                break;
            }
            
            if (isMaximal)
                maximalMerges[currentMerge] = currentCherryPicks;
        }

        // Collect commits for display - prioritize merge commits over individual cherry-picks
        var commitsToDisplay = new List<CommitInfo>();
        var commitsInMerges = new HashSet<string>();
        
        // Add merge commits first
        foreach (var (mergeSha, hashSet) in maximalMerges)
        {
            var mergeCommit = outstandingCommits.FirstOrDefault(c => c.Sha == mergeSha);
            if (mergeCommit == null) continue;
            commitsToDisplay.Add(mergeCommit);
                
            // Track which cherry-pick commits are included in merges
            foreach (var cherrySha in hashSet)
            {
                commitsInMerges.Add(cherrySha);
            }
        }
        
        // Add standalone cherry-pick commits (not covered by any merge)
        var standaloneCherryPicks = newCherryPickCommits
            .Where(c => !cherryPicksCoveredByMerge.Contains(c.Sha))
            .ToList();
        commitsToDisplay.AddRange(standaloneCherryPicks);
        
        // Remove any cherry-pick commits that are already included in merge commits
        commitsToDisplay = [.. commitsToDisplay.Where(c => 
            maximalMerges.ContainsKey(c.Sha) || // Keep merge commits
            !commitsInMerges.Contains(c.Sha)    // Keep cherry-picks not in merges
        )];
        
        // Build multi-ticket MR map
        var mergeCommitInfos = commitsToDisplay.Where(c => maximalMerges.ContainsKey(c.Sha)).ToList();
        var ticketToMrs = CherryPickHelper.BuildMrTicketMap(mergeCommitInfos, outstandingCommits, _repo);

        // Fetch Jira ticket info for all tickets found in the analysis
        Dictionary<string, CherryPickHelper.JiraTicketInfo> ticketInfos = [];
        try
        {
            var jiraConfig = CherryPickHelper.LoadOrCreateJiraConfig();
            var allTickets = ticketToMrs.Keys
                .Where(ticket => ticket != "No Ticket")
                .ToList();
            
            if (allTickets.Any())
            {
                AnsiConsole.MarkupLine("[blue]🔍 Fetching Jira ticket information...[/]");
                ticketInfos = await CherryPickHelper.FetchJiraTicketsBulkAsync(allTickets, jiraConfig);
                AnsiConsole.MarkupLine($"[green]✅ Fetched {ticketInfos.Count} tickets from Jira[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Could not fetch Jira tickets: {ex.Message}[/]");
        }

        // Create tree for ticket-centric display with Jira info
        var tree = new Spectre.Console.Tree("🎫 Ticket-Based Cherry-Pick Analysis")
            .Style(new Style(Color.Blue));

        await AnsiConsole.Live(tree)
            .AutoClear(true)
            .Start(_ =>
            {
                // Display grouped by ticket (multi-ticket aware) with Jira status
                foreach (var (ticketNumber, list) in ticketToMrs.OrderBy(g => g.Key))
                {
                    var mrInfos = list.Where(mr => mr.MrCommits.Count > 0).ToList();
                    if (mrInfos.Count == 0) continue; // Skip tickets with no real MRs

                    if (cancellationToken.IsCancellationRequested) break;

                    // Get Jira info for this ticket
                    var jiraInfo = ticketInfos.GetValueOrDefault(ticketNumber);
                    var ticketIcon = ticketNumber == "No Ticket" ? "❓" : "🎫";
                    var statusIcon = jiraInfo != null ? GetStatusIcon(jiraInfo.Status) : "";
                    var statusColor = jiraInfo != null ? GetStatusColor(jiraInfo.Status) : "white";
                    
                    // Build ticket header with Jira info
                    var ticketHeader = $"[bold yellow]{ticketIcon} {Markup.Escape(ticketNumber)}[/]";
                    if (jiraInfo != null)
                    {
                        ticketHeader += $" [{statusColor}]{statusIcon} {Markup.Escape(jiraInfo.Status)}[/]";
                        if (!string.IsNullOrEmpty(jiraInfo.Summary))
                        {
                            var summary = jiraInfo.Summary.Length > 60 ? jiraInfo.Summary.Substring(0, 57) + "..." : jiraInfo.Summary;
                            ticketHeader += $"\n   [dim]{Markup.Escape(summary)}[/]";
                        }
                    }
                    ticketHeader += $" [grey]({mrInfos.Count} MRs)[/]";
                    
                    var ticketNode = tree.AddNode(new TreeNode(new Markup(ticketHeader)));

                    foreach (var mrInfo in mrInfos)
                    {
                        // Find the merge commit info
                        var mergeCommit = outstandingCommits.FirstOrDefault(c => c.Sha == mrInfo.MergeCommitSha);
                        if (mergeCommit == null) continue;

                        var isRecent = (DateTimeOffset.UtcNow - mergeCommit.Date).TotalDays <= 7;
                        var commitColor = isRecent ? "bold blue" : "blue";
                        var mergeText = $"[{commitColor}]🔀 {Markup.Escape(mergeCommit.ShortSha)} {Markup.Escape(mergeCommit.Author)}: {Markup.Escape(mergeCommit.Message)}[/] [grey]({Markup.Escape(mergeCommit.Date.ToString("yyyy-MM-dd"))})[/]";
                        var mergeNode = ticketNode.AddNode(new TreeNode(new Markup(mergeText)));

                        // Show MR commits (if you want)
                        foreach (var mrText in mrInfo.MrCommits
                                     .Select(mrCommit => $"[dim cyan]  {Markup.Escape(mrCommit.ShortSha)} {Markup.Escape(mrCommit.Author)}: {Markup.Escape(mrCommit.Message)}[/] [grey]({Markup.Escape(mrCommit.Date.ToString("yyyy-MM-dd"))})[/]"))
                        {
                            mergeNode.AddNode(new TreeNode(new Markup(mrText)));
                        }
                    }
                }

                return Task.CompletedTask;
            });
        
        // Print the tree again so it remains visible after the live context
        AnsiConsole.Write(tree);

        analysis.Stats = new DiffStats
        {
            FilesChanged = analysis.ChangedFiles.Count,
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

    private static string GetStatusIcon(string status)
    {
        return status.ToLower() switch
        {
            "to do" => "📋",
            "in progress" => "🔄",
            "pending prod deployment" => "⏳",
            "prod deployed" => "✅",
            "done" => "✅",
            "closed" => "🔒",
            _ => "❓"
        };
    }

    private static string GetStatusColor(string status)
    {
        return status.ToLower() switch
        {
            "to do" => "blue",
            "in progress" => "yellow",
            "pending prod deployment" => "red",
            "prod deployed" => "green",
            "done" => "green",
            "closed" => "grey",
            _ => "white"
        };
    }
    #endregion
}
