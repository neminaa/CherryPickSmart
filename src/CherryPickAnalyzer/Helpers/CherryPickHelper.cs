using CherryPickAnalyzer.Models;
using Spectre.Console;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using FuzzySharp;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace CherryPickAnalyzer.Helpers;

public static class CherryPickHelper
{
    public static List<CommitInfo> SelectCommitsInteractively(List<CommitInfo> commits)
    {
        var prompt = new MultiSelectionPrompt<CommitInfo>()
            .Title("Select commits to cherry-pick:")
            .PageSize(10)
            .UseConverter(commit => $"{commit.ShortSha} {commit.Message} ({commit.Author})")
            .AddChoices(commits);

        return AnsiConsole.Prompt(prompt);
    }

    public static List<string> GenerateCherryPickCommands(string targetBranch, List<CommitInfo> commits)
    {
        var commands = new List<string> { $"git checkout {targetBranch}" };
        commands.AddRange(commits.Select(c => $"git cherry-pick {c.Sha}"));
        return commands;
    }

    public static void DisplayCherryPickCommands(List<string> commands)
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

    /// <summary>
    /// Extracts ticket numbers from commit messages using various HSAMED formats.
    /// Use this method to get all ticket keys for Jira lookups.
    /// </summary>
    /// <param name="message">The commit message to extract tickets from</param>
    /// <returns>List of unique ticket numbers found in the message</returns>
    public static List<string> ExtractTicketNumbers(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var tickets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedPrefix = "HSAMED";
        var minSimilarity = 85;

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
                if (dashIdx > 0)
                {
                    var prefix = raw.Substring(0, dashIdx);
                    var number = raw.Substring(dashIdx + 1);
                    // Fuzzy match prefix to expected
                    var sim = Fuzz.Ratio(prefix, expectedPrefix);
                    if (sim >= minSimilarity)
                    {
                        var ticket = $"{expectedPrefix}-{number}";
                        tickets.Add(ticket);
                    }
                }
            }
        }
        return [.. tickets.OrderBy(t => t)];
    }



    /// <summary>
    /// Returns the set of commit SHAs that are part of a merge request (MR), i.e., reachable from the feature branch parent but not from the target branch parent.
    /// </summary>
    /// <param name="repo">The repository</param>
    /// <param name="mergeCommit">The merge commit (MR)</param>
    /// <param name="maxDepth">Maximum ancestry depth to walk</param>
    /// <returns>Set of commit SHAs that are part of the MR</returns>
    public static HashSet<string> GetCommitsInMergeRequest(Repository repo, Commit mergeCommit, int maxDepth = 500)
    {
        if (mergeCommit.Parents.Count() < 2)
            return [];

        var parent1 = mergeCommit.Parents.ElementAt(0); // target branch tip before merge
        var parent2 = mergeCommit.Parents.ElementAt(1); // feature branch tip at merge

        // Walk ancestry of parent1 (target branch) to build exclusion set
        var targetAncestors = new HashSet<string>();
        var queue = new Queue<Commit>();
        queue.Enqueue(parent1);
        var depth = 0;
        while (queue.Count > 0 && depth < maxDepth)
        {
            var c = queue.Dequeue();
            if (!targetAncestors.Add(c.Sha)) continue;
            foreach (var p in c.Parents)
                queue.Enqueue(p);
            depth++;
        }

        // Walk ancestry of parent2 (feature branch) to build inclusion set
        var featureAncestors = new HashSet<string>();
        queue.Clear();
        queue.Enqueue(parent2);
        depth = 0;
        while (queue.Count > 0 && depth < maxDepth)
        {
            var c = queue.Dequeue();
            if (!featureAncestors.Add(c.Sha)) continue;
            foreach (var p in c.Parents)
                queue.Enqueue(p);
            depth++;
        }

        // MR commits = featureAncestors - targetAncestors
        featureAncestors.ExceptWith(targetAncestors);
        return featureAncestors;
    }

    /// <summary>
    /// Returns the list of commits on the first-parent path from the feature branch tip (parent2) to the merge base with the target branch (parent1).
    /// </summary>
    /// <param name="repo">The repository</param>
    /// <param name="mergeCommit">The merge commit (MR)</param>
    /// <returns>List of commits (most recent first) that are directly part of the MR</returns>
    public static List<Commit> GetMRCommitsFirstParentOnly(Repository repo, Commit mergeCommit)
    {
        if (mergeCommit == null || mergeCommit.Parents.Count() < 2)
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
        List<CommitInfo> mergeCommits,
        List<CommitInfo> outstandingCommits,
        Repository repo)
    {
        var ticketToMrs = new Dictionary<string, List<MrTicketInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mergeCommit in mergeCommits)
        {
            var mergeCommitObj = repo.Lookup<LibGit2Sharp.Commit>(mergeCommit.Sha);
            if (mergeCommitObj == null) continue;
            var mrCommits = GetMRCommitsFirstParentOnlyNonMerges(repo, mergeCommitObj);
            // Collect all tickets from MR and its commits
            var tickets = new List<string>();
            tickets.AddRange(ExtractTicketNumbers(mergeCommit.Message));
            tickets.AddRange(mrCommits.SelectMany(c => ExtractTicketNumbers(c.Message)));
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
            if (!correctedTickets.Any())
                correctedTickets.Add("No Ticket");
            var info = new MrTicketInfo
            {
                MergeCommitSha = mergeCommit.Sha ?? string.Empty,
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
        public string JiraDefaultProject { get; set; } = "";
    }

    public class JiraTicketInfo
    {
        public string Key { get; set; } = "";
        public string Status { get; set; } = "";
        public string Summary { get; set; } = "";
    }

    public static JiraConfig LoadJiraConfig()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configPath = Path.Combine(home, ".cherrypickanalyzer", "jira.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Jira config not found at {configPath}");
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<JiraConfig>(json) ?? new JiraConfig();
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
        
        AnsiConsole.Write("Default Jira Project Key (e.g., HSAMED): ");
        var defaultProject = Console.ReadLine()?.Trim() ?? "";
        
        var config = new JiraConfig
        {
            JiraBaseUrl = baseUrl,
            JiraUsername = username,
            JiraApiToken = apiToken,
            JiraDefaultProject = defaultProject
        };
        
        // Save config
        Directory.CreateDirectory(configDir);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
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

    public static async Task<JiraTicketInfo?> FetchJiraTicketAsync(string ticketKey, JiraConfig config)
    {
        using var client = new HttpClient();
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.JiraUsername}:{config.JiraApiToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        var url = $"{config.JiraBaseUrl}/rest/api/2/issue/{ticketKey}";
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var fields = doc.RootElement.GetProperty("fields");
        return new JiraTicketInfo
        {
            Key = ticketKey,
            Status = fields.GetProperty("status").GetProperty("name").GetString() ?? "",
            Summary = fields.GetProperty("summary").GetString() ?? ""
        };
    }

    public static async Task<Dictionary<string, JiraTicketInfo>> FetchJiraTicketsBulkAsync(List<string> ticketKeys, JiraConfig config)
    {
        if (!ticketKeys.Any()) return [];
        
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
}
