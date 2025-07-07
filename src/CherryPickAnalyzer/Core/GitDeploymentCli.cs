using CherryPickAnalyzer.Display;
using CherryPickAnalyzer.Helpers;
using CherryPickAnalyzer.Models;
using CherryPickAnalyzer.Options;
using CherryPickAnalyzer.Services;
using LibGit2Sharp;
using Spectre.Console;
using RepositoryStatus = CherryPickAnalyzer.Models.RepositoryStatus;

namespace CherryPickAnalyzer.Core;

public class GitDeploymentCli : IDisposable
{
    private readonly Repository _repo;
    private readonly GitCommandExecutor _gitExecutor;
    private readonly BranchValidator _branchValidator;
    private readonly RepositoryInfoDisplay _repoInfoDisplay;

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
        var excludeFiles = options.ExcludeFiles.Count() != 0 ? 
            new HashSet<string>(options.ExcludeFiles, StringComparer.OrdinalIgnoreCase) : 
            new HashSet<string>(defaultExcludes, StringComparer.OrdinalIgnoreCase);

        try
        {
            AnsiConsole.Write(new FigletText( "TSP GIT Helper")
                .LeftJustified()
                .Color(Color.Green3)
            
            );

            _repoInfoDisplay.DisplayRepositoryInfo(_repo.Head.FriendlyName, _repo.Network.Remotes.Select(r => r.Name));
            _repoInfoDisplay.DisplayRepositoryStatus();

            if (!options.NoFetch)
            {
                await _gitExecutor.FetchWithProgressAsync(options.Remote, cts.Token);
            }

            _branchValidator.ValidateBranches(options.SourceBranch, options.TargetBranch);

            var analysis = await AnalyzeWithProgressAsync(options.SourceBranch,
                options.TargetBranch, options.TicketPrefix, cts.Token);

            AnalysisDisplay.DisplaySuggestions(analysis, options.SourceBranch, options.TargetBranch);

            await ExportHtmlReportAsync(options, analysis, cts);


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

    private async Task ExportHtmlReportAsync(AnalyzeOptions options, DeploymentAnalysis analysis,
        CancellationTokenSource cts)
    {
        // HTML Export
        if (string.IsNullOrEmpty(options.OutputDir) && !analysis.HasContentDifferences) 
            return;
        try
        {
            Directory.SetCurrentDirectory(Environment.CurrentDirectory);
            var outputDir = options.OutputDir ?? Environment.CurrentDirectory;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"cherry-pick-analysis_{options.SourceBranch.Replace("/", "_")}_to_{options.TargetBranch.Replace("/", "_")}_{timestamp}.html";
            outputDir = Path.GetFullPath(outputDir);
            var outputPath = Path.Combine(outputDir, filename);
            
            // Ensure output directory exists
            Directory.CreateDirectory(outputDir);
            
            // Get repository info and Jira base URL
            var repoInfo = GetRepositoryInfo(options.Remote);
            var jiraBaseUrl = CherryPickHelper.LoadOrCreateJiraConfig().JiraBaseUrl;
            
            if (!string.IsNullOrEmpty(repoInfo.DisplayName))
            {
                AnsiConsole.MarkupLine($"[blue]📦 Repository: {repoInfo.DisplayName}[/]");
            }
            else if (!string.IsNullOrEmpty(repoInfo.Name))
            {
                AnsiConsole.MarkupLine($"[blue]📦 Repository: {repoInfo.Name}[/]");
            }
            if (!string.IsNullOrEmpty(repoInfo.HttpsUrl))
            {
                AnsiConsole.MarkupLine($"[blue]🔗 Repository URL: {repoInfo.HttpsUrl}[/]");
            }
            if (!string.IsNullOrEmpty(repoInfo.Host))
            {
                AnsiConsole.MarkupLine($"[blue]🌐 Host: {repoInfo.Host}[/]");
            }
            if (!string.IsNullOrEmpty(jiraBaseUrl))
            {
                AnsiConsole.MarkupLine($"[blue]🎫 Jira Base URL: {jiraBaseUrl}[/]");
            }
                    
            var html = HtmlExportService.GenerateHtml(analysis, options.SourceBranch, options.TargetBranch, repoInfo.HttpsUrl, jiraBaseUrl, repoInfo.DisplayName ?? repoInfo.Name);
            await File.WriteAllTextAsync(outputPath, html, cts.Token);
            var fileUrl = $"file:///{outputPath}";
            AnsiConsole.MarkupLine($"[green]✅ HTML report exported to:[/] [link={fileUrl}]{Markup.Escape(outputPath)}[/]");
            AnsiConsole.MarkupLine("\t\t\t\t[italic dim]Ctrl+Click to open the report[/]");
            AnsiConsole.WriteLine();
        } 
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
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
        string expectedPrefix,
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
                sourceBranch, analysis.OutstandingCommits, 
                analysis.CherryPickAnalysis.NewCommits, 
                expectedPrefix,
                cancellationToken);
        }

        return analysis;
    }

    private async Task<ContentAnalysis> GetDetailedContentAnalysisAsync(
        string sourceBranch,
        List<CommitInfo> outstandingCommits,
        List<CommitInfo> newCherryPickCommits,
        string expectedPrefix,
        CancellationToken cancellationToken = default)
    {

        var analysis = new ContentAnalysis { ChangedFiles = [] };
        const int totalAdded = 0;
        const int totalDeleted = 0;

        // Build set of cherry-pick commit SHAs for quick lookup
        var cherryPickCommitShas = new HashSet<string>(
            newCherryPickCommits
                .Select(c => c.Sha));

        // Precompute merge commit coverage using maximal/superset approach
        var mergeToCherryPicks = new Dictionary<string, HashSet<string>>();
        var cherryPicksCoveredByMerge = new HashSet<string>();

        // For ancestry mode: collect merge SHAs in first-parent history of source branch
        var firstParentMerges = new HashSet<string>();
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

        // Find merge commits that include cherry-pick commits
        foreach (var commitInfo in outstandingCommits)
        {
            var commit = _repo.Lookup<Commit>(commitInfo.Sha);
            if (commit == null || commit.Parents.Count() <= 1) continue;

            // Only consider merge commits in first-parent history for ancestry mode
            if (!firstParentMerges.Contains(commit.Sha))
                continue;

            var foundCherryPicks = new HashSet<string>();


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
        var ticketToMrs = CherryPickHelper.BuildMrTicketMap(expectedPrefix,mergeCommitInfos, outstandingCommits, _repo);

        // Process standalone cherry-pick commits (not covered by any merge) and group them by ticket
        var unmergedCherryPicks = newCherryPickCommits
            .Where(c => !cherryPicksCoveredByMerge.Contains(c.Sha))
            .ToList();

        // Collect ALL tickets (both from MRs and standalone commits) before fetching Jira info
        var allTickets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add tickets from merge requests
        allTickets.UnionWith(ticketToMrs.Keys);
        
        // Add tickets from standalone commits
        foreach (var commit in unmergedCherryPicks)
        {
            var tickets = CherryPickHelper.ExtractTicketNumbers(expectedPrefix, commit.Message);
            allTickets.UnionWith(tickets);
        }

        // Fetch Jira ticket info for all tickets found in the analysis
        Dictionary<string, CherryPickHelper.JiraTicketInfo> ticketInfos = [];
        try
        {
            var jiraConfig = CherryPickHelper.LoadOrCreateJiraConfig();
            if (allTickets.Count != 0)
            {
                AnsiConsole.MarkupLine("[blue]🔍 Fetching Jira ticket information...[/]");
                ticketInfos = await CherryPickHelper.FetchJiraTicketsBulkAsync(allTickets.ToList(), jiraConfig);
                AnsiConsole.MarkupLine($"[green]✅ Fetched {ticketInfos.Count} tickets from Jira[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Could not fetch Jira tickets: {ex.Message}[/]");
        }

        // Populate ContentAnalysis with tree data for cherry-pick command generation
        analysis.TicketGroups = [];

        foreach (var (ticketNumber, list) in ticketToMrs.OrderBy(g => g.Key))
        {
            var mrInfos = list.Where(mr => mr.MrCommits.Count > 0).ToList();
            if (mrInfos.Count == 0) continue; // Skip tickets with no real MRs

            var ticketGroup = new TicketGroup
            {
                TicketNumber = ticketNumber,
                JiraInfo = ticketInfos.GetValueOrDefault(ticketNumber),
                MergeRequests = [],
                StandaloneCommits = []
            };

            foreach (var mrInfo in mrInfos)
            {
                // Find the merge commit info
                var mergeCommit = outstandingCommits.FirstOrDefault(c => c.Sha == mrInfo.MergeCommitSha);
                if (mergeCommit == null) continue;

                var mergeRequestInfo = new MergeRequestInfo
                {
                    MergeCommit = mergeCommit,
                    MrCommits = mrInfo.MrCommits,
                    CherryPickShas = [.. maximalMerges.GetValueOrDefault(mergeCommit.Sha, [])]
                };

                ticketGroup.MergeRequests.Add(mergeRequestInfo);
            }

            analysis.TicketGroups.Add(ticketGroup);
        }

        if (unmergedCherryPicks.Count != 0)
        {
            // Group standalone commits by their ticket numbers
            var standaloneByTicket = new Dictionary<string, List<CommitInfo>>(StringComparer.OrdinalIgnoreCase);
            var noTicketCommits = new List<CommitInfo>();

            foreach (var commit in unmergedCherryPicks)
            {
                var tickets = CherryPickHelper.ExtractTicketNumbers(expectedPrefix,commit.Message);
                if (tickets.Count != 0)
                {
                    // Use the first ticket found (or could be more sophisticated)
                    var primaryTicket = tickets.First();
                    if (!standaloneByTicket.ContainsKey(primaryTicket))
                        standaloneByTicket[primaryTicket] = [];
                    standaloneByTicket[primaryTicket].Add(commit);
                }
                else
                {
                    noTicketCommits.Add(commit);
                }
            }

            // Add standalone commits to their respective ticket groups
            foreach (var (ticketNumber, commits) in standaloneByTicket)
            {
                var existingGroup = analysis.TicketGroups.FirstOrDefault(g => g.TicketNumber == ticketNumber);
                if (existingGroup == null)
                {
                    // Create new ticket group for standalone commits
                    existingGroup = new TicketGroup 
                    { 
                        TicketNumber = ticketNumber,
                        JiraInfo = ticketInfos.GetValueOrDefault(ticketNumber)
                    };
                    analysis.TicketGroups.Add(existingGroup);
                }
                existingGroup.StandaloneCommits.AddRange(commits);
            }

            // Add truly no-ticket commits to "No Ticket" group
            if (noTicketCommits.Count != 0)
            {
                var noTicketGroup = analysis.TicketGroups.FirstOrDefault(g => g.TicketNumber == "No Ticket");
                if (noTicketGroup == null)
                {
                    noTicketGroup = new TicketGroup { TicketNumber = "No Ticket" };
                    analysis.TicketGroups.Add(noTicketGroup);
                }
                noTicketGroup.StandaloneCommits.AddRange(noTicketCommits);
            }
        }

        // Create tree for ticket-centric display with Jira info
        var tree = new Spectre.Console.Tree("🎫 Ticket-Based Cherry-Pick Analysis")
            .Style(new Style(Color.Blue));

        await AnsiConsole.Live(tree)
            .AutoClear(true)
            .Start(_ =>
            {
                // Display grouped by ticket (multi-ticket aware) with Jira status
                foreach (var ticketGroup in analysis.TicketGroups)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    // Get Jira info for this ticket
                    var jiraInfo = ticketGroup.JiraInfo;
                    var ticketIcon = ticketGroup.TicketNumber == "No Ticket" ? "❓" : "🎫";
                    var statusIcon = jiraInfo != null ? GetStatusIcon(jiraInfo.Status) : "";
                    var statusColor = jiraInfo != null ? GetStatusColor(jiraInfo.Status) : "white";

                    // Build ticket header with Jira info
                    var ticketHeader = $"[bold yellow]{ticketIcon} {Markup.Escape(ticketGroup.TicketNumber)}[/]";
                    if (jiraInfo != null)
                    {
                        ticketHeader += $" [{statusColor}]{statusIcon} {Markup.Escape(jiraInfo.Status)}[/]";
                        if (!string.IsNullOrEmpty(jiraInfo.Summary))
                        {
                            var summary = jiraInfo.Summary.Length > 60 ? string.Concat(jiraInfo.Summary.AsSpan(0, 57), "...") : jiraInfo.Summary;
                            ticketHeader += $"\n   [dim]{Markup.Escape(summary)}[/]";
                        }
                    }
                    ticketHeader += $" [grey]({ticketGroup.MergeRequests.Count} MRs, {ticketGroup.StandaloneCommits.Count} standalone)[/]";

                    var ticketNode = tree.AddNode(new TreeNode(new Markup(ticketHeader)));

                    // Display merge requests
                    foreach (var mrInfo in ticketGroup.MergeRequests)
                    {
                        var isRecent = (DateTimeOffset.UtcNow - mrInfo.MergeCommit.Date).TotalDays <= 7;
                        var commitColor = isRecent ? "bold blue" : "blue";
                        var mergeText = $"[{commitColor}]🔀 {Markup.Escape(mrInfo.MergeCommit.ShortSha)} {Markup.Escape(mrInfo.MergeCommit.Author)}: {Markup.Escape(mrInfo.MergeCommit.Message)}[/] [grey]({Markup.Escape(mrInfo.MergeCommit.Date.ToString("yyyy-MM-dd"))})[/]";
                        var mergeNode = ticketNode.AddNode(new TreeNode(new Markup(mergeText)));

                        // Show MR commits (if you want)
                        foreach (var mrText in mrInfo.MrCommits
                                     .Select(mrCommit => $"[dim cyan]  {Markup.Escape(mrCommit.ShortSha)} {Markup.Escape(mrCommit.Author)}: {Markup.Escape(mrCommit.Message)}[/] [grey]({Markup.Escape(mrCommit.Date.ToString("yyyy-MM-dd"))})[/]"))
                        {
                            mergeNode.AddNode(new TreeNode(new Markup(mrText)));
                        }
                    }

                    // Display standalone commits
                    foreach (var standaloneCommit in ticketGroup.StandaloneCommits)
                    {
                        var isRecent = (DateTimeOffset.UtcNow - standaloneCommit.Date).TotalDays <= 7;
                        var commitColor = isRecent ? "bold green" : "green";
                        var standaloneText = $"[{commitColor}]📝 {Markup.Escape(standaloneCommit.ShortSha)} {Markup.Escape(standaloneCommit.Author)}: {Markup.Escape(standaloneCommit.Message)}[/] [grey]({Markup.Escape(standaloneCommit.Date.ToString("yyyy-MM-dd"))})[/]";
                        ticketNode.AddNode(new TreeNode(new Markup(standaloneText)));
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
    
    private string? GetRepositoryUrl()
    {
        try
        {
            var origin = _repo.Network.Remotes.FirstOrDefault(r => r.Name == "origin");
            if (origin?.Url != null)
            {
                var url = origin.Url;
                if (url.StartsWith("git@"))
                {
                    // Convert git@host:owner/repo.git to https://host/owner/repo
                    // Example: git@git.tsp.dev:hsa-share/medics/applications.git
                    // Should become: https://git.tsp.dev/hsa-share/medics/applications
                    
                    // Remove "git@" prefix
                    url = url.Substring(4);
                    
                    // Find the colon and replace it with "/"
                    var colonIndex = url.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var host = url.Substring(0, colonIndex);
                        var path = url.Substring(colonIndex + 1);
                        
                        // Remove .git suffix if present
                        if (path.EndsWith(".git"))
                        {
                            path = path.Substring(0, path.Length - 4);
                        }
                        
                        url = $"https://{host}/{path}";
                    }
                }
                return url;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Could not get repository URL: {ex.Message}[/]");
        }
        return null;
    }
    
    private RepositoryInfo GetRepositoryInfo(string remote)
    {
        var info = new RepositoryInfo();
        
        try
        {
            // Method 1: From remote origin URL (most reliable)
            var origin = _repo.Network.Remotes.FirstOrDefault(r => r.Name == remote);
            if (origin?.Url != null)
            {
                info.Url = origin.Url;
                info.Name = ExtractRepoNameFromUrl(origin.Url);
                info.DisplayName = GenerateDisplayNameFromUrl(origin.Url);
                info.Host = ExtractHostFromUrl(origin.Url);
            }
            
            // Method 2: From working directory name (fallback)
            if (string.IsNullOrEmpty(info.Name))
            {
                info.Name = Path.GetFileName(_repo.Info.WorkingDirectory);
            }
            
            
            // Convert SSH URLs to HTTPS for display
            if (!string.IsNullOrEmpty(info.Url) && info.Url.StartsWith("git@"))
            {
                info.HttpsUrl = ConvertSshToHttps(info.Url);
            }
            else
            {
                info.HttpsUrl = info.Url;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️  Could not get repository info: {ex.Message}[/]");
        }
        
        return info;
    }
    
    private static string? ExtractRepoNameFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        
        // Remove .git suffix
        var cleanUrl = url.Replace(".git", "");
        
        // Handle SSH format: git@host:owner/repo
        if (cleanUrl.StartsWith("git@"))
        {
            var parts = cleanUrl.Substring(4).Split(':');
            if (parts.Length == 2)
            {
                var pathParts = parts[1].Split('/');
                return pathParts[^1]; // Last part is the repo name
            }
        }
        
        // Handle HTTPS format: https://host/owner/repo
        if (cleanUrl.StartsWith("http"))
        {
            var uri = new Uri(cleanUrl);
            var pathParts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length >= 2)
            {
                return pathParts[^1]; // Last part is the repo name
            }
        }
        
        return null;
    }
    
    private static string? GenerateDisplayNameFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        
        // Remove .git suffix
        var cleanUrl = url.Replace(".git", "");
        
        string? path = null;
        
        // Handle SSH format: git@host:owner/repo
        if (cleanUrl.StartsWith("git@"))
        {
            var parts = cleanUrl.Substring(4).Split(':');
            if (parts.Length == 2)
            {
                path = parts[1];
            }
        }
        
        // Handle HTTPS format: https://host/owner/repo
        if (cleanUrl.StartsWith("http"))
        {
            var uri = new Uri(cleanUrl);
            path = uri.AbsolutePath.TrimStart('/');
        }
        
        if (string.IsNullOrEmpty(path)) return null;
        
        // Convert path to display name
        // Example: hsa-share/common/share-common-workflow -> HSA SHARE Common Workflow
        var pathParts = path.Split('/');
        var displayParts = new List<string>();
        
        foreach (var part in pathParts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            
            // Handle kebab-case (e.g., share-common-workflow)
            var words = part.Split('-');
            var titleWords = words.Select(word => 
            {
                if (string.IsNullOrEmpty(word)) return word;
                
                // Handle acronyms (e.g., HSA, PDF, API)
                if (word.All(char.IsUpper) && word.Length <= 5)
                {
                    return word;
                }
                
                // Title case for regular words
                return char.ToUpper(word[0]) + word.Substring(1).ToLower();
            });
            
            displayParts.AddRange(titleWords);
        }
        
        // Remove duplicates while preserving order
        var uniqueParts = new List<string>();
        foreach (var part in displayParts)
        {
            if (!uniqueParts.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                uniqueParts.Add(part);
            }
        }
        
        return string.Join(" ", uniqueParts);
    }
    
    private static string? ExtractHostFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        
        // Handle SSH format: git@host:owner/repo
        if (url.StartsWith("git@"))
        {
            var parts = url.Substring(4).Split(':');
            if (parts.Length == 2)
            {
                return parts[0];
            }
        }
        
        // Handle HTTPS format: https://host/owner/repo
        if (url.StartsWith("http"))
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        
        return null;
    }
    
    private static string? ConvertSshToHttps(string sshUrl)
    {
        if (string.IsNullOrEmpty(sshUrl) || !sshUrl.StartsWith("git@"))
            return sshUrl;
            
        // Convert git@host:owner/repo.git to https://host/owner/repo
        var url = sshUrl.Substring(4); // Remove "git@"
        var colonIndex = url.IndexOf(':');
        if (colonIndex > 0)
        {
            var host = url.Substring(0, colonIndex);
            var path = url.Substring(colonIndex + 1);
            
            // Remove .git suffix if present
            if (path.EndsWith(".git"))
            {
                path = path.Substring(0, path.Length - 4);
            }
            
            return $"https://{host}/{path}";
        }
        
        return sshUrl;
    }
    
    public class RepositoryInfo
    {
        public string? Name { get; set; }           // Short name (e.g., "share-common-workflow")
        public string? DisplayName { get; set; }    // Human-readable name (e.g., "HSA SHARE Common Share Common Workflow")
        public string? Url { get; set; }            // Original URL (SSH or HTTPS)
        public string? HttpsUrl { get; set; }       // Converted HTTPS URL
        public string? Host { get; set; }           // Host (e.g., "git.tsp.dev")
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
            _ => "🔄"
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
