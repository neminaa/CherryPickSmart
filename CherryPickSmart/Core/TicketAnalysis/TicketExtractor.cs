using System.Text.RegularExpressions;
using CherryPickSmart.Models;

namespace CherryPickSmart.Core.TicketAnalysis;

public class TicketExtractor
{
    public record TicketPattern(string Name, Regex CompiledRegex, Func<Match, string?> ExtractTicket);

    private readonly List<TicketPattern> _patterns;
    private readonly HashSet<string> _validPrefixes;

    public TicketExtractor(List<string>? validPrefixes = null)
    {
        _validPrefixes = (validPrefixes ?? ["HSAMED"]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pre-compile regexes for better performance
        _patterns =
        [
            new TicketPattern(
                "JIRA_STANDARD",
                new Regex(@"([A-Z]{2,10}-\d{1,6})", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => match.Groups[1].Value.ToUpperInvariant()
            ),

            new TicketPattern(
                "FLEXIBLE",
                new Regex(@"(\w{2,10})[\s-](\d{1,6})", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
            ),

            new TicketPattern(
                "BRACKETED",
                new Regex(@"\[(\w{2,10})[\s-](\d{1,6})\]", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
            ),

            new TicketPattern(
                "HASHTAG",
                new Regex(@"#(\w{2,10})[\s-]?(\d{1,6})", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
            ),

            // Handle tickets in branch names or PR titles
            new TicketPattern(
                "BRANCH_STYLE",
                new Regex(@"(?:feature|bugfix|hotfix|fix|feat|bug|chore)[/_]([A-Z]{2,10})[/_-]?(\d{1,6})", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
            ),

            // Common typos/variations with colons or other separators
            new TicketPattern(
                "TYPO_VARIATIONS",
                new Regex(@"([A-Z]{2,10})\s*[:#]\s*(\d{1,6})", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
            ),

            // Parentheses style
            new TicketPattern(
                "PARENTHESES",
                new Regex(@"\((\w{2,10})[\s-](\d{1,6})\)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                match => $"{match.Groups[1].Value.ToUpperInvariant()}-{match.Groups[2].Value}"
            )
        ];
    }

    /// <summary>
    /// Extract ticket IDs from text using configured patterns
    /// </summary>
    /// <param name="text">Text to search for tickets (commit message, branch name, etc.)</param>
    /// <returns>List of unique, validated ticket IDs sorted alphabetically</returns>
    public List<string> ExtractTickets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var tickets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in _patterns)
        {
            var matches = pattern.CompiledRegex.Matches(text);

            foreach (Match match in matches)
            {
                var ticketKey = pattern.ExtractTicket(match);

                if (!string.IsNullOrEmpty(ticketKey) && IsValidTicket(ticketKey))
                {
                    tickets.Add(ticketKey);
                }
            }
        }

        return tickets.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Validate that a ticket has a valid prefix and reasonable number
    /// </summary>
    private bool IsValidTicket(string ticketKey)
    {
        if (string.IsNullOrEmpty(ticketKey))
            return false;

        var parts = ticketKey.Split('-');
        if (parts.Length != 2)
            return false;

        var prefix = parts[0];
        var numberStr = parts[1];

        // Check prefix is in valid list
        if (!_validPrefixes.Contains(prefix))
            return false;

        // Check number is valid and within reasonable bounds
        if (!int.TryParse(numberStr, out var number) || number <= 0 || number > 999999)
            return false;

        return true;
    }

    /// <summary>
    /// Build a map from ticket IDs to commits that reference them
    /// </summary>
    public Dictionary<string, List<CpCommit>> BuildTicketCommitMap(CpCommitGraph graph)
    {
        var ticketToCommits = new Dictionary<string, List<CpCommit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, commit) in graph.Commits)
        {
            // Extract from commit message
            var messageTickets = ExtractTickets(commit.Message);

            // Also extract from branch name if available
            List<string> branchTickets = [];
                //!string.IsNullOrEmpty(commit.BranchName)
                //? ExtractTickets(commit.BranchName)
                //: new List<string>();

            // Combine all tickets found
            var allTickets = messageTickets.Concat(branchTickets).Distinct().ToList();

            foreach (var ticket in allTickets)
            {
                if (!ticketToCommits.ContainsKey(ticket))
                    ticketToCommits[ticket] = [];

                ticketToCommits[ticket].Add(commit);
                commit.ExtractedTickets.Add(ticket);
            }
        }

        return ticketToCommits;
    }

    /// <summary>
    /// Get statistics about ticket extraction across all commits
    /// </summary>
    public TicketExtractionStats GetExtractionStats(CpCommitGraph graph)
    {
        var ticketMap = BuildTicketCommitMap(graph);
        var commitsWithTickets = graph.Commits.Values.Count(c => c.ExtractedTickets.Count > 0);
        var commitsWithoutTickets = graph.Commits.Count - commitsWithTickets;

        return new TicketExtractionStats
        {
            TotalCommits = graph.Commits.Count,
            CommitsWithTickets = commitsWithTickets,
            CommitsWithoutTickets = commitsWithoutTickets,
            UniqueTickets = ticketMap.Keys.Count,
            TicketCoverage = graph.Commits.Count > 0 ? (double)commitsWithTickets / graph.Commits.Count : 0,
            TicketsWithMultipleCommits = ticketMap.Count(kvp => kvp.Value.Count > 1),
            AverageCommitsPerTicket = ticketMap.Count > 0 ? (double)ticketMap.Values.Sum(list => list.Count) / ticketMap.Count : 0
        };
    }

    /// <summary>
    /// Find commits that might be related to a ticket but don't explicitly reference it
    /// Useful for finding cherry-pick candidates
    /// </summary>
    public List<CpCommit> FindRelatedCommits(CpCommitGraph graph, string ticketId, int maxResults = 10)
    {
        if (!IsValidTicket(ticketId))
            return [];

        var directMatches = graph.Commits.Values
            .Where(c => c.ExtractedTickets.Contains(ticketId, StringComparer.OrdinalIgnoreCase))
            .ToHashSet();

        // Find commits with similar file patterns
        var ticketCommits = graph.Commits.Values
            .Where(c => c.ExtractedTickets.Contains(ticketId, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (ticketCommits.Count == 0)
            return [];

        // Get common file patterns from ticket commits
        var commonFiles = ticketCommits
            .SelectMany(c => c.ModifiedFiles)
            .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find commits that modify similar files but aren't already matched
        var relatedCommits = graph.Commits.Values
            .Where(c => !directMatches.Contains(c))
            .Where(c => c.ModifiedFiles.Any(f => commonFiles.Contains(f)))
            .OrderByDescending(c => c.ModifiedFiles.Count(f => commonFiles.Contains(f)))
            .Take(maxResults)
            .ToList();

        return relatedCommits;
    }

    /// <summary>
    /// Add a custom pattern for ticket extraction
    /// </summary>
    public void AddCustomPattern(string name, string regexPattern, Func<Match, string?> extractor)
    {
        var compiledRegex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        _patterns.Add(new TicketPattern(name, compiledRegex, extractor));
    }

    /// <summary>
    /// Get all valid prefixes
    /// </summary>
    public IReadOnlySet<string> ValidPrefixes => _validPrefixes.ToHashSet();
}

/// <summary>
/// Statistics about ticket extraction from commits
/// </summary>
public record TicketExtractionStats
{
    public int TotalCommits { get; init; }
    public int CommitsWithTickets { get; init; }
    public int CommitsWithoutTickets { get; init; }
    public int UniqueTickets { get; init; }
    public double TicketCoverage { get; init; } // Percentage of commits with tickets
    public int TicketsWithMultipleCommits { get; init; }
    public double AverageCommitsPerTicket { get; init; }
}
