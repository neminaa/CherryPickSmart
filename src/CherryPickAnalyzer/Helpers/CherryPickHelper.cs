using CherryPickAnalyzer.Models;
using Spectre.Console;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using FuzzySharp;

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
    /// Extracts ticket numbers from commit messages using various HSAMED formats
    /// </summary>
    /// <param name="message">The commit message to extract tickets from</param>
    /// <returns>List of unique ticket numbers found in the message</returns>
    public static List<string> ExtractTicketNumbers(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new List<string>();

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
                    int sim = Fuzz.Ratio(prefix, expectedPrefix);
                    if (sim >= minSimilarity)
                    {
                        var ticket = $"{expectedPrefix}-{number}";
                        tickets.Add(ticket);
                    }
                }
            }
        }
        return tickets.OrderBy(t => t).ToList();
    }

    /// <summary>
    /// Groups commits by their primary ticket number, with fuzzy correction for typos using FuzzySharp.
    /// </summary>
    /// <param name="commits">List of commits to group</param>
    /// <param name="mergeToCherryPicks">Dictionary mapping merge commits to their included cherry-pick commits</param>
    /// <returns>Dictionary of ticket number to list of commits</returns>
    public static Dictionary<string, List<CommitInfo>> GroupCommitsByTicket(
        List<CommitInfo> commits,
        Dictionary<string, HashSet<string>> mergeToCherryPicks = null,
        Repository repo = null)
    {
        var ticketGroups = new Dictionary<string, List<CommitInfo>>();
        var noTicketCommits = new List<CommitInfo>();
        var ticketToCommits = new Dictionary<string, List<CommitInfo>>();
        var allTickets = new List<string>();

        // First pass: group commits that have their own ticket numbers
        foreach (var commit in commits)
        {
            var tickets = ExtractTicketNumbers(commit.Message);
            if (tickets.Any())
            {
                var primaryTicket = tickets.First();
                if (!ticketToCommits.ContainsKey(primaryTicket))
                    ticketToCommits[primaryTicket] = new List<CommitInfo>();
                ticketToCommits[primaryTicket].Add(commit);
                allTickets.Add(primaryTicket);
            }
            else
            {
                noTicketCommits.Add(commit);
            }
        }

        // --- MR ticket inference from MR and its commits (robust fuzzy logic) ---
        if (repo != null)
        {
            foreach (var commit in commits)
            {
                // Look up the real commit object
                var realCommit = repo.Lookup<LibGit2Sharp.Commit>(commit.Sha);
                if (realCommit == null) continue;
                if (realCommit.Parents.Count() > 1)
                {
                    // Collect all tickets from MR message and its MR commits
                    var ticketsInMR = new List<string>();
                    ticketsInMR.AddRange(ExtractTicketNumbers(commit.Message));
                    var mrCommits = GetMRCommitsFirstParentOnlyNonMerges(repo, realCommit);
                    ticketsInMR.AddRange(mrCommits.SelectMany(c => ExtractTicketNumbers(c.Message)));
                    if (ticketsInMR.Any())
                    {
                        // Fuzzy-correct all tickets to the most common/correct form
                        var mrKnownTickets = ticketsInMR.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var correctedTickets = ticketsInMR
                            .Select(ticket =>
                            {
                                var best = mrKnownTickets
                                    .Select(t => new { Ticket = t, Score = Fuzz.Ratio(ticket, t) })
                                    .OrderByDescending(x => x.Score)
                                    .FirstOrDefault(x => x.Score >= 90);
                                return best != null ? best.Ticket : ticket;
                            })
                            .ToList();
                        // Use the most common/corrected ticket
                        var finalTicket = correctedTickets
                            .GroupBy(t => t)
                            .OrderByDescending(g => g.Count())
                            .First().Key;
                        if (!ticketToCommits.ContainsKey(finalTicket))
                            ticketToCommits[finalTicket] = new List<CommitInfo>();
                        ticketToCommits[finalTicket].Add(commit);
                        allTickets.Add(finalTicket);
                        // Remove from noTicketCommits if present
                        noTicketCommits.Remove(commit);
                    }
                }
            }
        }

        // Build set of common/known tickets (appearing more than once)
        var knownTickets = ticketToCommits.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Fuzzy correction for rare/typo tickets
        var correctedTicketToCommits = new Dictionary<string, List<CommitInfo>>();
        foreach (var kvp in ticketToCommits)
        {
            var ticket = kvp.Key;
            var commitsForTicket = kvp.Value;
            string corrected = ticket;
            if (!knownTickets.Contains(ticket))
            {
                var best = knownTickets
                    .Select(t => new { Ticket = t, Score = Fuzz.Ratio(ticket, t) })
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault(x => x.Score >= 90);
                if (best != null)
                    corrected = best.Ticket;
            }
            if (!correctedTicketToCommits.ContainsKey(corrected))
                correctedTicketToCommits[corrected] = new List<CommitInfo>();
            correctedTicketToCommits[corrected].AddRange(commitsForTicket);
        }

        // Second pass: assign tickets to commits without tickets based on their merge commit
        if (mergeToCherryPicks != null)
        {
            foreach (var commit in noTicketCommits.ToList())
            {
                string assignedTicket = null;
                foreach (var mergeEntry in mergeToCherryPicks)
                {
                    if (mergeEntry.Value.Contains(commit.Sha))
                    {
                        var mergeCommit = commits.FirstOrDefault(c => c.Sha == mergeEntry.Key);
                        if (mergeCommit != null)
                        {
                            var mergeTickets = ExtractTicketNumbers(mergeCommit.Message);
                            if (mergeTickets.Any())
                            {
                                assignedTicket = mergeTickets.First();
                                break;
                            }
                        }
                    }
                }
                if (assignedTicket != null)
                {
                    if (!correctedTicketToCommits.ContainsKey(assignedTicket))
                        correctedTicketToCommits[assignedTicket] = new List<CommitInfo>();
                    correctedTicketToCommits[assignedTicket].Add(commit);
                    noTicketCommits.Remove(commit);
                }
            }
        }

        if (noTicketCommits.Any())
            correctedTicketToCommits["No Ticket"] = noTicketCommits;

        return correctedTicketToCommits;
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
        if (mergeCommit == null || mergeCommit.Parents.Count() < 2)
            return new HashSet<string>();

        var parent1 = mergeCommit.Parents.ElementAt(0); // target branch tip before merge
        var parent2 = mergeCommit.Parents.ElementAt(1); // feature branch tip at merge

        // Walk ancestry of parent1 (target branch) to build exclusion set
        var targetAncestors = new HashSet<string>();
        var queue = new Queue<Commit>();
        queue.Enqueue(parent1);
        int depth = 0;
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
            return new List<Commit>();

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
        return all.Where(c => c.Parents.Count() == 1).ToList(); // Only non-merge commits
    }

    public class MrTicketInfo
    {
        public string MergeCommitSha { get; set; } = string.Empty;
        public List<string> Tickets { get; set; } = new();
        public List<CommitInfo> MrCommits { get; set; } = new();
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
                MrCommits = mrCommits
                    .Select(c => outstandingCommits.FirstOrDefault(ci => ci.Sha == c.Sha))
                    .Where(ci => ci != null)
                    .Select(ci => ci!) // null-forgiving operator
                    .ToList()
            };
            foreach (var ticket in correctedTickets)
            {
                if (!ticketToMrs.ContainsKey(ticket))
                    ticketToMrs[ticket] = new List<MrTicketInfo>();
                ticketToMrs[ticket].Add(info);
            }
        }
        return ticketToMrs;
    }
}
