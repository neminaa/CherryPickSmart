using System.Text.RegularExpressions;
using CherryPickSmart.Models;
using CherryPickSmart.Core.TicketAnalysis;

namespace CherryPickSmart.Core.TicketAnalysis;

public class OrphanCommitDetector
{
    private readonly TicketExtractor _ticketExtractor;
    private readonly Regex _malformedTicketRegex;

    public record OrphanCommit
    {
        public CpCommit Commit { get; init; } = null!;
        public List<TicketSuggestion> Suggestions { get; init; } = [];
        public string Reason { get; init; } = ""; // Why it's orphaned
        public OrphanSeverity Severity { get; init; }
    }

    public record TicketSuggestion
    {
        public string TicketKey { get; init; } = "";
        public double Confidence { get; init; } // 0-100
        public List<string> Reasons { get; init; } = [];
        public SuggestionType Type { get; init; }
    }

    public enum OrphanSeverity
    {
        Low,        // Minor issue, likely intentional (merge commits, etc.)
        Medium,     // Should probably have a ticket
        High,       // Definitely should have a ticket
        Critical    // Malformed ticket reference
    }

    public enum SuggestionType
    {
        ExistingTicket,     // Suggest an existing ticket
        NewTicket,          // Suggest creating a new ticket
        FixMalformed,       // Fix a malformed ticket reference
        FilePattern,        // Based on file patterns
        MessageSimilarity   // Based on similar commit messages
    }

    public OrphanCommitDetector(TicketExtractor ticketExtractor)
    {
        _ticketExtractor = ticketExtractor;

        // Build regex pattern from valid prefixes
        var prefixes = string.Join("|", _ticketExtractor.ValidPrefixes.Select(Regex.Escape));
        _malformedTicketRegex = new Regex(
            $@"(?i)({prefixes})[\s_]*(\d{{1,6}})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
    }

    /// <summary>
    /// Find orphaned commits and generate suggestions for each
    /// </summary>
    public List<OrphanCommit> FindOrphans(
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var orphans = new List<OrphanCommit>();

        // Performance: Use HashSet for O(1) lookups instead of O(n)
        var commitsWithTickets = ticketCommitMap.Values
            .SelectMany(commits => commits.Select(c => c.Sha))
            .ToHashSet();

        foreach (var (sha, commit) in graph.Commits)
        {
            // Skip commits that already have tickets
            if (commitsWithTickets.Contains(sha))
                continue;

            // Skip certain commit types that don't need tickets
            if (ShouldSkipCommit(commit))
                continue;

            var (reason, severity) = DetermineOrphanReason(commit);
            var suggestions = GenerateSuggestions(commit, graph, ticketCommitMap);

            orphans.Add(new OrphanCommit
            {
                Commit = commit,
                Reason = reason,
                Severity = severity,
                Suggestions = suggestions
            });
        }

        return orphans.OrderByDescending(o => o.Severity).ThenByDescending(o => o.Commit.Timestamp).ToList();
    }

    /// <summary>
    /// Determine if a commit should be skipped (doesn't need a ticket)
    /// </summary>
    private static bool ShouldSkipCommit(CpCommit commit)
    {
        var message = commit.Message.ToLowerInvariant();

        // Skip merge commits (they usually don't need separate tickets)
        if (commit.IsMergeCommit)
            return true;

        // Skip automated commits
        var automatedPatterns = new[]
        {
            "merge branch",
            "merge pull request",
            "bump version",
            "update dependencies",
            "auto-generated",
            "automated",
            "ci:",
            "cd:",
            "[automated]",
            "version bump"
        };

        return automatedPatterns.Any(pattern => message.Contains(pattern));
    }

    /// <summary>
    /// Determine why a commit is orphaned and its severity
    /// </summary>
    private (string reason, OrphanSeverity severity) DetermineOrphanReason(CpCommit commit)
    {
        var message = commit.Message;

        // Check for malformed ticket references
        if (_malformedTicketRegex.IsMatch(message))
        {
            return ("Malformed ticket reference detected - missing hyphen or wrong format",
                    OrphanSeverity.Critical);
        }

        // Check for partial ticket patterns
        if (Regex.IsMatch(message, @"\b[A-Z]{2,10}\s*\d+", RegexOptions.IgnoreCase))
        {
            return ("Possible ticket reference without proper format", OrphanSeverity.High);
        }

        // Check message length and quality
        if (message.Length < 10)
        {
            return ("Commit message too short - likely needs proper description and ticket",
                    OrphanSeverity.High);
        }

        if (message.Length < 30)
        {
            return ("Short commit message without ticket reference", OrphanSeverity.Medium);
        }

        // Check for code changes that typically need tickets
        if (commit.ModifiedFiles.Any(IsBusinessLogicFile))
        {
            return ("Business logic changes without ticket reference", OrphanSeverity.High);
        }

        return ("No ticket reference found", OrphanSeverity.Medium);
    }

    /// <summary>
    /// Check if a file is likely business logic that should have a ticket
    /// </summary>
    private static bool IsBusinessLogicFile(string filePath)
    {
        var path = filePath.ToLowerInvariant();

        // Files that typically indicate business changes
        var businessIndicators = new[]
        {
            "/controllers/", "/services/", "/business/", "/domain/",
            "/models/", "/entities/", "/repositories/", "/handlers/"
        };

        var hasBusinessPath = businessIndicators.Any(indicator => path.Contains(indicator));

        // Exclude test files, docs, and config
        var exclusions = new[] { "test", "spec", ".md", ".txt", ".json", ".xml", ".config" };
        var isExcluded = exclusions.Any(exclusion => path.Contains(exclusion));

        return hasBusinessPath && !isExcluded;
    }

    /// <summary>
    /// Generate suggestions for how to fix an orphaned commit
    /// </summary>
    private List<TicketSuggestion> GenerateSuggestions(
        CpCommit orphanCommit,
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();

        // Suggest fixing malformed ticket references
        suggestions.AddRange(SuggestMalformedFixes(orphanCommit));

        // Suggest existing tickets based on file patterns
        suggestions.AddRange(SuggestTicketsFromFilePatterns(orphanCommit, ticketCommitMap));

        // Suggest tickets based on similar commit messages
        //suggestions.AddRange(SuggestTicketsFromSimilarMessages(orphanCommit, ticketCommitMap));

        // Suggest tickets from related commits (same author, nearby time)
        suggestions.AddRange(SuggestTicketsFromRelatedCommits(orphanCommit, graph, ticketCommitMap));

        return suggestions
            .OrderByDescending(s => s.Confidence)
            .Take(5) // Top 5 suggestions
            .ToList();
    }

    /// <summary>
    /// Suggest fixes for malformed ticket references
    /// </summary>
    private List<TicketSuggestion> SuggestMalformedFixes(CpCommit commit)
    {
        var suggestions = new List<TicketSuggestion>();
        var matches = _malformedTicketRegex.Matches(commit.Message);

        foreach (Match match in matches)
        {
            var prefix = match.Groups[1].Value.ToUpperInvariant();
            var number = match.Groups[2].Value;
            var fixedTicket = $"{prefix}-{number}";

            suggestions.Add(new TicketSuggestion
            {
                TicketKey = fixedTicket,
                Confidence = 95,
                Type = SuggestionType.FixMalformed,
                Reasons = [$"Fix malformed reference '{match.Value}' to '{fixedTicket}'"]
            });
        }

        return suggestions;
    }

    /// <summary>
    /// Suggest tickets based on files modified in similar commits
    /// </summary>
    private List<TicketSuggestion> SuggestTicketsFromFilePatterns(
        CpCommit orphanCommit,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();
        var orphanFiles = orphanCommit.ModifiedFiles.ToHashSet();

        foreach (var (ticketKey, commits) in ticketCommitMap)
        {
            var ticketFiles = commits.SelectMany(c => c.ModifiedFiles).ToHashSet();
            var overlap = orphanFiles.Intersect(ticketFiles).Count();

            if (overlap > 0)
            {
                var confidence = Math.Min(95, (double)overlap / orphanFiles.Count * 100);
                var sharedFiles = orphanFiles.Intersect(ticketFiles).Take(3).ToList();

                suggestions.Add(new TicketSuggestion
                {
                    TicketKey = ticketKey,
                    Confidence = confidence,
                    Type = SuggestionType.FilePattern,
                    Reasons = [$"Modifies same files: {string.Join(", ", sharedFiles)}"]
                });
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Suggest tickets based on similar commit messages
    /// </summary>
    private List<TicketSuggestion> SuggestTicketsFromSimilarMessages(
        CpCommit orphanCommit,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();
        var orphanWords = ExtractKeywords(orphanCommit.Message);

        foreach (var (ticketKey, commits) in ticketCommitMap)
        {
            var maxSimilarity = 0.0;
            var bestMatch = "";

            foreach (var commit in commits)
            {
                var similarity = CalculateMessageSimilarity(orphanWords, commit.Message);
                if (similarity > maxSimilarity)
                {
                    maxSimilarity = similarity;
                    bestMatch = commit.Message.Split('\n')[0]; // First line
                }
            }

            if (maxSimilarity > 0.3) // 30% similarity threshold
            {
                suggestions.Add(new TicketSuggestion
                {
                    TicketKey = ticketKey,
                    Confidence = maxSimilarity * 100,
                    Type = SuggestionType.MessageSimilarity,
                    Reasons = [$"Similar to: \"{bestMatch.Substring(0, Math.Min(50, bestMatch.Length))}...\""]
                });
            }
        }

        return suggestions;
    }

    /// <summary>
    /// Suggest tickets from commits by same author around the same time
    /// </summary>
    private List<TicketSuggestion> SuggestTicketsFromRelatedCommits(
        CpCommit orphanCommit,
        CpCommitGraph graph,
        Dictionary<string, List<CpCommit>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();
        var timeWindow = TimeSpan.FromDays(7); // Look within a week

        var relatedCommits = graph.Commits.Values
            .Where(c => c.Author == orphanCommit.Author)
            .Where(c => Math.Abs((c.Timestamp - orphanCommit.Timestamp).TotalDays) <= timeWindow.TotalDays)
            .Where(c => c.ExtractedTickets.Any())
            .ToList();

        var ticketCounts = relatedCommits
            .SelectMany(c => c.ExtractedTickets)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (ticketKey, count) in ticketCounts.OrderByDescending(kvp => kvp.Value))
        {
            var confidence = Math.Min(80, count * 20); // Higher confidence for more commits

            suggestions.Add(new TicketSuggestion
            {
                TicketKey = ticketKey,
                Confidence = confidence,
                Type = SuggestionType.ExistingTicket,
                Reasons = [$"Same author worked on this ticket {count} times recently"]
            });
        }

        return suggestions.Take(3).ToList();
    }

    /// <summary>
    /// Extract meaningful keywords from a commit message
    /// </summary>
    private static HashSet<string> ExtractKeywords(string message)
    {
        var words = Regex.Split(message.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length > 3) // Skip short words
            .Where(w => !IsStopWord(w))
            .ToHashSet();

        return words;
    }

    /// <summary>
    /// Check if a word is a common stop word
    /// </summary>
    private static bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "had", "her", "was", "one", "our", "out", "day", "get", "has", "him", "his", "how", "man", "new", "now", "old", "see", "two", "way", "who", "boy", "did", "its", "let", "put", "say", "she", "too", "use"
        };
        return stopWords.Contains(word);
    }

    /// <summary>
    /// Calculate similarity between commit messages using keyword overlap
    /// </summary>
    private static double CalculateMessageSimilarity(HashSet<string> orphanWords, string compareMessage)
    {
        var compareWords = ExtractKeywords(compareMessage);

        if (!orphanWords.Any() || !compareWords.Any())
            return 0;

        var intersection = orphanWords.Intersect(compareWords).Count();
        var union = orphanWords.Union(compareWords).Count();

        return (double)intersection / union; // Jaccard similarity
    }
}