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
        
        // Regex patterns for various HSAMED formats
        var patterns = new[]
        {
            @"HSAMED-\d+",           // HSAMED-1234
            @"\[HSAMED-\d+\]",       // [HSAMED-1234]
            @"HSAMED\s+\d+",         // HSAMED 1234
            @"hsamed\s+\d+",         // hsamed 1234
            @"hsamed-\d+"            // hsamed-1234
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(message, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var ticket = match.Value.ToUpperInvariant();
                // Remove brackets if present
                ticket = ticket.Replace("[", "").Replace("]", "");
                tickets.Add(ticket);
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
    public static Dictionary<string, List<CommitInfo>> GroupCommitsByTicket(List<CommitInfo> commits, Dictionary<string, HashSet<string>> mergeToCherryPicks = null)
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

        // Build set of common/known tickets (appearing more than once)
        var knownTickets = ticketToCommits.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Fuzzy correction for rare/typo tickets
        var correctedTicketToCommits = new Dictionary<string, List<CommitInfo>>();
        foreach (var kvp in ticketToCommits)
        {
            var ticket = kvp.Key;
            var commitsForTicket = kvp.Value;
            // If this ticket is not in the known set (i.e., typo or rare), try to correct
            string corrected = ticket;
            if (!knownTickets.Contains(ticket))
            {
                // Find the closest known ticket by FuzzySharp
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

        // Add the remaining no-ticket group if it has commits
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
}
