using CherryPickAnalyzer.Models;
using Spectre.Console;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using FuzzySharp;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Text;

namespace CherryPickAnalyzer.Helpers;

public static partial class CherryPickHelper
{
    private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
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

    /// <summary>
    /// Extracts ticket numbers from commit messages using various HSAMED formats.
    /// Use this method to get all ticket keys for Jira lookups.
    /// </summary>
    /// <param name="expectedPrefix">HSAMED</param>
    /// <param name="message">The commit message to extract tickets from</param>
    /// <returns>List of unique ticket numbers found in the message</returns>
    public static List<string> ExtractTicketNumbers(string expectedPrefix, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var tickets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int minSimilarity = 85;

        // Regex patterns for possible ticket formats (including typos)
        var patterns = new[]
        {
            @"[A-Za-z]{5,7}-\d+",           // e.g. HSAMED-1234, hsmaed-1234, etc.
            @"[A-Za-z]{5,7}\s+\d+",         // e.g. HSAMED 1234, hsmaed 1234, etc.
            @"\[[A-Za-z]{5,7}-\d+\]",      // e.g. [HSAMED-1234]
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(message, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var raw = match.Value.ToUpperInvariant().Replace("[", "").Replace("]", "").Replace(" ", "-");
                // Split prefix and number
                var dashIdx = raw.IndexOf('-');
                if (dashIdx <= 0) continue;
                var prefix = raw[..dashIdx];
                var number = raw[(dashIdx + 1)..];
                // Fuzzy match prefix to expected
                var sim = Fuzz.Ratio(prefix, expectedPrefix);
                if (sim < minSimilarity) continue;
                var ticket = $"{expectedPrefix}-{number}";
                tickets.Add(ticket);
            }
        }
        return [.. tickets.OrderBy(t => t)];
    }


    /// <summary>
    /// Returns the list of commits on the first-parent path from the feature branch tip (parent2) to the merge base with the target branch (parent1).
    /// </summary>
    /// <param name="repo">The repository</param>
    /// <param name="mergeCommit">The merge commit (MR)</param>
    /// <returns>List of commits (most recent first) that are directly part of the MR</returns>
    public static List<Commit> GetMRCommitsFirstParentOnly(Repository repo, Commit mergeCommit)
    {
        if (mergeCommit.Parents.Count() < 2)
            return [];

        var parent1 = mergeCommit.Parents.ElementAt(0); // target branch tip before merge
        var parent2 = mergeCommit.Parents.ElementAt(1); // feature branch tip at merge

        // Find merge base
        var mergeBase = repo.ObjectDatabase.FindMergeBase(parent1, parent2);
        var commits = new List<Commit>();
        var current = parent2;
        while (current != null && current != mergeBase)
        {
            commits.Add(current);
            if (!current.Parents.Any())
                break;
            current = current.Parents.First(); // Only follow first parent
        }
        return commits;
    }

    /// <summary>
    /// Returns the list of non-merge (single-parent) commits on the first-parent path from the feature branch tip (parent2) to the merge base with the target branch (parent1).
    /// </summary>
    /// <param name="repo">The repository</param>
    /// <param name="mergeCommit">The merge commit (MR)</param>
    /// <returns>List of non-merge commits (most recent first) that are directly part of the MR</returns>
    public static List<Commit> GetMRCommitsFirstParentOnlyNonMerges(Repository repo, Commit mergeCommit)
    {
        var all = GetMRCommitsFirstParentOnly(repo, mergeCommit);
        return [.. all.Where(c => c.Parents.Count() == 1)]; // Only non-merge commits
    }

    public class MrTicketInfo
    {
        public string MergeCommitSha { get; set; } = string.Empty;
        public List<string> Tickets { get; set; } = [];
        public List<CommitInfo> MrCommits { get; set; } = [];
    }

    public static Dictionary<string, List<MrTicketInfo>> BuildMrTicketMap(
        string expectedPrefix,
        List<CommitInfo> mergeCommits,
        List<CommitInfo> outstandingCommits,
        Repository repo)
    {
        var ticketToMrs = new Dictionary<string, List<MrTicketInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mergeCommit in mergeCommits)
        {
            var mergeCommitObj = repo.Lookup<Commit>(mergeCommit.Sha);
            if (mergeCommitObj == null) continue;
            var mrCommits = GetMRCommitsFirstParentOnlyNonMerges(repo, mergeCommitObj);
            // Collect all tickets from MR and its commits
            var tickets = new List<string>();
            tickets.AddRange(ExtractTicketNumbers(expectedPrefix,mergeCommit.Message));
            tickets.AddRange(mrCommits.SelectMany(c => ExtractTicketNumbers(expectedPrefix,c.Message)));
            // Fuzzy-correct all tickets
            var mrKnownTickets = tickets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var correctedTickets = tickets
                .Select(ticket =>
                {
                    var best = mrKnownTickets
                        .Select(t => new { Ticket = t, Score = Fuzz.Ratio(ticket, t) })
                        .OrderByDescending(x => x.Score)
                        .FirstOrDefault(x => x.Score >= 90);
                    return best != null ? best.Ticket : ticket;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (correctedTickets.Count == 0)
                correctedTickets.Add("No Ticket");
            var info = new MrTicketInfo
            {
                MergeCommitSha = mergeCommit.Sha,
                Tickets = correctedTickets,
                MrCommits = [.. mrCommits
                    .Select(c => outstandingCommits.FirstOrDefault(ci => ci.Sha == c.Sha))
                    .Where(ci => ci != null)
                    .Select(ci => ci!)]
            };
            foreach (var ticket in correctedTickets)
            {
                if (!ticketToMrs.ContainsKey(ticket))
                    ticketToMrs[ticket] = [];
                ticketToMrs[ticket].Add(info);
            }
        }
        return ticketToMrs;
    }

    public class JiraConfig
    {
        public string JiraBaseUrl { get; set; } = "";
        public string JiraUsername { get; set; } = "";
        public string JiraApiToken { get; set; } = "";
    }

    public class JiraTicketInfo
    {
        public string Key { get; set; } = "";
        public string Status { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public static JiraConfig LoadOrCreateJiraConfig()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configDir = Path.Combine(home, ".cherrypickanalyzer");
        var configPath = Path.Combine(configDir, "jira.json");
        
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<JiraConfig>(json) ?? new JiraConfig();
        }
        
        // Prompt user for config
        return PromptForJiraConfig(configDir, configPath);
    }


    private static JiraConfig PromptForJiraConfig(string configDir, string configPath)
    {
        AnsiConsole.WriteLine("Jira configuration not found. Please provide your Jira credentials:");
        AnsiConsole.WriteLine();

        AnsiConsole.Write("Jira Base URL (e.g., https://yourcompany.atlassian.net): ");
        var baseUrl = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(baseUrl))
            throw new InvalidOperationException("Jira Base URL is required");

        AnsiConsole.Write("Jira Username (email): ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(username))
            throw new InvalidOperationException("Jira Username is required");

        AnsiConsole.Write("Jira API Token: ");
        var apiToken = ReadPassword();
        if (string.IsNullOrEmpty(apiToken))
            throw new InvalidOperationException("Jira API Token is required");

        var config = new JiraConfig
        {
            JiraBaseUrl = baseUrl,
            JiraUsername = username,
            JiraApiToken = apiToken,
        };

        // Save config
        Directory.CreateDirectory(configDir);
        var json = JsonSerializer.Serialize(config, CachedJsonSerializerOptions);
        File.WriteAllText(configPath, json);

        AnsiConsole.WriteLine($"Configuration saved to {configPath}");
        return config;
    }

    private static string ReadPassword()
    {
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                password.Length--;
            else if (!char.IsControl(key.KeyChar))
                password.Append(key.KeyChar);
        }
        Console.WriteLine();
        return password.ToString();
    }

    public static async Task<Dictionary<string, JiraTicketInfo>> FetchJiraTicketsBulkAsync(List<string> ticketKeys, JiraConfig config)
    {
        if (ticketKeys.Count == 0) return [];
        
        using var client = new HttpClient();
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.JiraUsername}:{config.JiraApiToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        
        var result = new Dictionary<string, JiraTicketInfo>();
        const int batchSize = 50;
        
        // Process tickets in batches of 50
        for (var i = 0; i < ticketKeys.Count; i += batchSize)
        {
            var batch = ticketKeys.Skip(i).Take(batchSize).ToList();
            
            // Build JQL query for bulk search
            var jql = $"key IN ({string.Join(",", batch.Select(k => $"\"{k}\""))})";
            var url = $"{config.JiraBaseUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields=key,status,summary&maxResults={batchSize}";
            
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) continue;
            
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var issues = doc.RootElement.GetProperty("issues");
            
            foreach (var issue in issues.EnumerateArray())
            {
                var key = issue.GetProperty("key").GetString() ?? "";
                var fields = issue.GetProperty("fields");
                var ticketInfo = new JiraTicketInfo
                {
                    Key = key,
                    Status = fields.GetProperty("status").GetProperty("name").GetString() ?? "",
                    Summary = fields.GetProperty("summary").GetString() ?? ""
                };
                result[key] = ticketInfo;
            }
        }
        
        return result;
    }

    public static List<string> SelectTicketsInteractively(ContentAnalysis contentAnalysis)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("Interactive Ticket Selection").RuleStyle("blue"));

        // Create status-based options
        var statusOptions = new List<string>();
        var ticketsByStatus = contentAnalysis.TicketGroups
            .Where(tg => tg.TicketNumber != "No Ticket")
            .GroupBy(tg => tg.JiraInfo?.Status ?? "Unknown")
            .OrderBy(g => GetStatusPriority(g.Key))
            .ToList();

        foreach (var group in ticketsByStatus)
        {
            statusOptions.Add($"{GetStatusIcon(group.Key)} {group.Key} ({group.Count()} tickets)");
        }

        // Add "No Ticket" option if exists
        var noTicketGroup = contentAnalysis.TicketGroups.FirstOrDefault(tg => tg.TicketNumber == "No Ticket");
        if (noTicketGroup != null)
        {
            statusOptions.Add("❓ No Ticket (1 group)");
        }

        var selectedTickets = new List<string>();

        // Step 1: Selection method
        var selectionMethod = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose selection method:")
                .PageSize(15)
                .AddChoices("Select by Status", "Manual Selection", "Select All", "Select Ready for Deployment", "Skip Selection")
        );

        switch (selectionMethod)
        {
            case "Skip Selection":
                return [];
            case "Select All":
                return [.. contentAnalysis.TicketGroups.Select(tg => tg.TicketNumber)];
            case "Select Ready for Deployment":
                return [.. contentAnalysis.TicketGroups
                    .Where(tg => IsReadyForDeployment(tg.JiraInfo?.Status))
                    .Select(tg => tg.TicketNumber)];
            case "Select by Status":
            {
                // Step 2: Multi-status selection
                var statusChoices = statusOptions.Select(status => 
                {
                    var statusName = status.Substring(status.IndexOf(' ') + 1, status.IndexOf('(') - status.IndexOf(' ') - 1).Trim();
                    return new StatusChoice { StatusName = statusName, DisplayText = status };
                }).ToList();

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[blue]Available statuses:[/]");
                foreach (var choice in statusChoices)
                {
                    AnsiConsole.MarkupLine($"  {choice.DisplayText}");
                }
                AnsiConsole.WriteLine();

                var statusPrompt = new MultiSelectionPrompt<StatusChoice>()
                    .Title("Select one or more statuses (use SPACE to select, ENTER to confirm):")
                    .PageSize(15)
                    .UseConverter(item => item.DisplayText)
                    .AddChoices(statusChoices);

                var selectedStatuses = AnsiConsole.Prompt(statusPrompt);
            
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]Selected {selectedStatuses.Count} status(es):[/]");
            
                if (selectedStatuses.Count != 0)
                {
                    foreach (var statusChoice in selectedStatuses)
                    {
                        AnsiConsole.MarkupLine($"  • {statusChoice.StatusName}");
                    
                        if (statusChoice.StatusName == "No Ticket")
                        {
                            // Select "No Ticket" group
                            var ticketId = $"NO-TICKET-{Guid.NewGuid():N}";
                            selectedTickets.Add(ticketId);
                            AnsiConsole.MarkupLine($"    → Added No Ticket group: {ticketId}");
                        }
                        else
                        {
                            // Status-based selection
                            var matchingTickets = contentAnalysis.TicketGroups
                                .Where(tg =>
                                    tg.TicketNumber != "No Ticket" &&
                                    (tg.JiraInfo?.Status ?? "Unknown") == statusChoice.StatusName)
                                .ToArray();
                        
                            AnsiConsole.MarkupLine($"    [dim]Looking for tickets with status: '{statusChoice.StatusName}'[/]");
                            AnsiConsole.MarkupLine($"    [dim]Found {matchingTickets.Length} matching tickets[/]");
                        
                            foreach (var ticketGroup in matchingTickets)
                            {
                                selectedTickets.Add(ticketGroup.TicketNumber);
                                AnsiConsole.MarkupLine($"    → Added ticket: {ticketGroup.TicketNumber}");
                            }
                        
                            if (matchingTickets.Length == 0)
                            {
                                AnsiConsole.MarkupLine($"    [yellow]⚠️  No tickets found with status '{statusChoice.StatusName}'[/]");
                                // Debug: show all available statuses
                                var allStatuses = contentAnalysis.TicketGroups
                                    .Where(tg => tg.TicketNumber != "No Ticket" && tg.JiraInfo != null)
                                    .Select(tg => tg.JiraInfo!.Status)
                                    .Distinct()
                                    .OrderBy(s => s)
                                    .ToList();
                                AnsiConsole.MarkupLine($"    [dim]Available statuses: {string.Join(", ", allStatuses)}[/]");
                            }
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]No statuses selected. Returning to main menu.[/]");
                    return SelectTicketsInteractively(contentAnalysis); // Recursive call to restart
                }

                break;
            }
            case "Manual Selection":
            {
                // Step 2: Manual ticket selection
                var ticketChoices = contentAnalysis.TicketGroups.Select(tg => 
                {
                    var ticketId = tg.TicketNumber == "No Ticket" ? $"NO-TICKET-{Guid.NewGuid():N}" : tg.TicketNumber;
                    var statusIcon = GetStatusIcon(tg.JiraInfo?.Status ?? "Unknown");
                    var summary = tg.JiraInfo?.Summary ?? "";
                    if (summary.Length > 40) summary = summary[..37] + "...";
                
                    var displayText = tg.TicketNumber == "No Ticket" 
                        ? $"{statusIcon} {ticketId} | {tg.MergeRequests.Count} MRs, {tg.StandaloneCommits.Count} standalone"
                        : $"{statusIcon} {Markup.Escape(tg.TicketNumber)} | {Markup.Escape(tg.JiraInfo?.Status ?? "Unknown")} | {Markup.Escape(summary)} | {tg.MergeRequests.Count} MRs, {tg.StandaloneCommits.Count} standalone";
                
                    return new TicketChoice { DisplayText = displayText, TicketId = ticketId };
                }).ToList();

                var prompt = new MultiSelectionPrompt<TicketChoice>()
                    .Title("Select individual tickets:")
                    .PageSize(15)
                    .UseConverter(item => item.DisplayText)
                    .AddChoices(ticketChoices);

                var selectedItems = AnsiConsole.Prompt(prompt);
                selectedTickets.AddRange(selectedItems.Select(item => item.TicketId));
                break;
            }
        }

        // Step 3: Show dependencies and confirm selection
        if (selectedTickets.Count == 0) return selectedTickets;
        var dependencies = FindDependencies(contentAnalysis, selectedTickets);
        if (dependencies.Count == 0) return selectedTickets;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            "The following dependent tickets will also be included:\n" +
            string.Join("\n", dependencies.Select(d => $"• {d}"))
        ).BorderColor(Color.Yellow));

        var includeDependencies = AnsiConsole.Confirm("Include dependent tickets?");
        if (includeDependencies)
        {
            selectedTickets.AddRange(dependencies);
            selectedTickets = [.. selectedTickets.Distinct()];
        }

        // Display final selection summary
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            $"Selected {selectedTickets.Count} tickets for cherry-pick:\n" +
            string.Join("\n", selectedTickets.Select(t => $"• {t}"))
        ).BorderColor(Color.Green));

        return selectedTickets;
    }

    private class TicketChoice
    {
        public string DisplayText { get; init; } = "";
        public string TicketId { get; init; } = "";
    }

    private class StatusChoice
    {
        public string StatusName { get; init; } = "";
        public string DisplayText { get; init; } = "";
    }

    private static bool IsReadyForDeployment(string? status)
    {
        if (string.IsNullOrEmpty(status)) return false;
        
        return status.ToLower() switch
        {
            "done" => true,
            "prod deployed" => true,
            "ready for deployment" => true,
            "approved" => true,
            _ => false
        };
    }

    private static List<string> FindDependencies(ContentAnalysis contentAnalysis, List<string> selectedTickets)
    {
        var dependencies = new HashSet<string>();
        
        // This is a simplified dependency check - you might want to enhance this
        // based on your specific Jira workflow and ticket relationships
        foreach (var ticketId in selectedTickets)
        {
            var ticketGroup = contentAnalysis.TicketGroups.FirstOrDefault(tg => 
                tg.TicketNumber == ticketId || 
                (tg.TicketNumber == "No Ticket" && ticketId.StartsWith("NO-TICKET-")));
            
            if (ticketGroup?.JiraInfo != null)
            {
                // Check for common dependency patterns in the summary
                var summary = ticketGroup.JiraInfo.Summary.ToLower();
                if (summary.Contains("depends on") || summary.Contains("blocked by"))
                {
                    // Extract potential ticket references
                    var ticketMatches = TicketKeyRegex().Matches(summary);
                    foreach (Match match in ticketMatches)
                    {
                        var depTicket = match.Value;
                        if (!selectedTickets.Contains(depTicket))
                        {
                            dependencies.Add(depTicket);
                        }
                    }
                }
            }
        }
        
        return [.. dependencies];
    }

    private static int GetStatusPriority(string status)
    {
        return status.ToLower() switch
        {
            "to do" => 1,
            "in progress" => 2,
            "pending prod deployment" => 3,
            "prod deployed" => 4,
            "done" => 5,
            "closed" => 6,
            "unknown" => 99,
            _ => 50
        };
    }

    [GeneratedRegex(@"[A-Za-z]{2,}-\d+")]
    private static partial Regex TicketKeyRegex();
}
