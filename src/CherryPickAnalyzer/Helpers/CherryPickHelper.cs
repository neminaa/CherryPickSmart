using CherryPickAnalyzer.Models;
using Spectre.Console;
using System.Text.RegularExpressions;
using LibGit2Sharp;

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
    /// Groups commits by their primary ticket number, with fallback to merge commit tickets
    /// </summary>
    /// <param name="commits">List of commits to group</param>
    /// <param name="mergeToCherryPicks">Dictionary mapping merge commits to their included cherry-pick commits</param>
    /// <returns>Dictionary of ticket number to list of commits</returns>
    public static Dictionary<string, List<CommitInfo>> GroupCommitsByTicket(List<CommitInfo> commits, Dictionary<string, HashSet<string>> mergeToCherryPicks = null)
    {
        var ticketGroups = new Dictionary<string, List<CommitInfo>>();
        var noTicketCommits = new List<CommitInfo>();

        // First pass: group commits that have their own ticket numbers
        foreach (var commit in commits)
        {
            var tickets = ExtractTicketNumbers(commit.Message);
            
            if (tickets.Any())
            {
                // Use the first ticket as the primary grouping key
                var primaryTicket = tickets.First();
                if (!ticketGroups.ContainsKey(primaryTicket))
                    ticketGroups[primaryTicket] = new List<CommitInfo>();
                ticketGroups[primaryTicket].Add(commit);
            }
            else
            {
                // Commits without tickets go to a temporary list for second pass
                noTicketCommits.Add(commit);
            }
        }

        // Second pass: assign tickets to commits without tickets based on their merge commit
        if (mergeToCherryPicks != null)
        {
            foreach (var commit in noTicketCommits.ToList())
            {
                // Find if this commit is included in any merge commit
                string assignedTicket = null;
                
                foreach (var mergeEntry in mergeToCherryPicks)
                {
                    if (mergeEntry.Value.Contains(commit.Sha))
                    {
                        // This commit is included in a merge, check if the merge has a ticket
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
                    // Assign this commit to the merge's ticket group
                    if (!ticketGroups.ContainsKey(assignedTicket))
                        ticketGroups[assignedTicket] = new List<CommitInfo>();
                    ticketGroups[assignedTicket].Add(commit);
                    noTicketCommits.Remove(commit);
                }
            }
        }

        // Add the remaining no-ticket group if it has commits
        if (noTicketCommits.Any())
            ticketGroups["No Ticket"] = noTicketCommits;

        return ticketGroups;
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
}
