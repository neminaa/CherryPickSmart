using System.Text.RegularExpressions;
using CherryPickSmart.Models;

namespace CherryPickSmart.Core.TicketAnalysis;

public class TicketExtractor
{
    private readonly List<TicketPattern> _patterns =
    [
        new(@"([A-Z]{2,10}-\d{1,6})", "JIRA_STANDARD"), // HSAMED-1234
        new(@"(?i)(\w{2,10})[\s-](\d{1,6})", "FLEXIBLE"), // hsamed 1234, hsamed-1234
        new(@"\[(\w{2,10})[\s-](\d{1,6})\]", "BRACKETED") // [hsamed 1234]
    ];

    public record TicketPattern(string Regex, string Name);

    private readonly List<string> _validPrefixes;

    public TicketExtractor(List<string>? validPrefixes = null)
    {
        _validPrefixes = validPrefixes ?? ["HSAMED"];
    }

    public List<string> ExtractTickets(string text)
    {
        var tickets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in _patterns)
        {
            var regex = new Regex(pattern.Regex, RegexOptions.IgnoreCase);
            var matches = regex.Matches(text);

            foreach (Match match in matches)
            {
                var ticketKey = string.Empty;

                if (pattern.Name == "JIRA_STANDARD")
                {
                    ticketKey = match.Groups[1].Value.ToUpper();
                }
                else if (pattern.Name == "FLEXIBLE")
                {
                    var prefix = match.Groups[1].Value.ToUpper();
                    var number = match.Groups[2].Value;
                    ticketKey = $"{prefix}-{number}";
                }
                else if (pattern.Name == "BRACKETED")
                {
                    var prefix = match.Groups[1].Value.ToUpper();
                    var number = match.Groups[2].Value;
                    ticketKey = $"{prefix}-{number}";
                }

                if (IsValidTicketPrefix(ticketKey))
                    tickets.Add(ticketKey);
            }
        }

        return tickets.OrderBy(t => t).ToList();
    }

    private bool IsValidTicketPrefix(string ticketKey)
    {
        var prefix = ticketKey.Split('-')[0];
        return _validPrefixes.Contains(prefix);
    }

    public Dictionary<string, List<CpCommit>> BuildTicketCommitMap(CpCommitGraph graph)
    {
        var ticketToCommits = new Dictionary<string, List<CpCommit>>();

        foreach (var (sha, commit) in graph.Commits)
        {
            var messageTickets = ExtractTickets(commit.Message);

            foreach (var ticket in messageTickets)
            {
                if (!ticketToCommits.ContainsKey(ticket))
                    ticketToCommits[ticket] = [];

                ticketToCommits[ticket].Add(commit);
                commit.ExtractedTickets.Add(ticket);
            }
        }

        return ticketToCommits;
    }
}
