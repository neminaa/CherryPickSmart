# Smart Cherry-Pick CLI Tool - Copilot Agent PRD

## Project Overview
Build a .NET 8 CLI tool that intelligently manages Git cherry-picking operations based on Jira ticket selection, minimizing conflicts and preserving merge commit structure.

**Project Name**: `cherry-pick-smart`  
**Primary Command**: `cps` (cherry-pick-smart)  
**Language**: C# / .NET 8  
**Initial Scope**: CLI-only, no HTML generation

---

## PROJECT STRUCTURE
```
/src/CherryPickSmart/
├── Program.cs
├── Commands/
│   ├── AnalyzeCommand.cs
│   ├── PlanCommand.cs
│   ├── ExecuteCommand.cs
│   └── ConfigCommand.cs
├── Core/
│   ├── GitAnalysis/
│   │   ├── CommitGraph.cs
│   │   ├── GitHistoryParser.cs
│   │   └── MergeCommitAnalyzer.cs
│   ├── TicketAnalysis/
│   │   ├── TicketExtractor.cs
│   │   ├── OrphanCommitDetector.cs
│   │   └── TicketInferenceEngine.cs
│   ├── ConflictAnalysis/
│   │   ├── ConflictPredictor.cs
│   │   └── OrderOptimizer.cs
│   └── Integration/
│       ├── JiraClient.cs
│       └── GitCommandExecutor.cs
├── Models/
│   ├── Commit.cs
│   ├── Ticket.cs
│   ├── CherryPickPlan.cs
│   └── ConflictPrediction.cs
├── Services/
│   ├── ConfigurationService.cs
│   └── InteractivePromptService.cs
└── CherryPickSmart.csproj
```

---

## TASK 1: Core Git Analysis Foundation

### Task 1.1: Create Basic Models
**File**: `src/CherryPickSmart/Models/Commit.cs`

```csharp
namespace CherryPickSmart.Models;

public record Commit
{
    public string Sha { get; init; } = "";
    public string ShortSha => Sha[..8];
    public List<string> ParentShas { get; init; } = new();
    public string Message { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public List<string> ModifiedFiles { get; init; } = new();
    public bool IsMergeCommit => ParentShas.Count > 1;
    
    // Derived properties
    public List<string> ExtractedTickets { get; set; } = new();
    public string? InferredTicket { get; set; }
    public double InferenceConfidence { get; set; }
}

public record CommitGraph
{
    public Dictionary<string, Commit> Commits { get; init; } = new();
    public Dictionary<string, List<string>> ChildrenMap { get; init; } = new();
    public string FromBranch { get; init; } = "";
    public string ToBranch { get; init; } = "";
}
```

### Task 1.2: Git History Parser
**File**: `src/CherryPickSmart/Core/GitAnalysis/GitHistoryParser.cs`

```csharp
namespace CherryPickSmart.Core.GitAnalysis;

public class GitHistoryParser
{
    private readonly ILogger<GitHistoryParser> _logger;
    
    public async Task<CommitGraph> ParseHistoryAsync(string fromBranch, string toBranch)
    {
        // GIT COMMAND: git log deploy/uat..deploy/dev --pretty=format:"==%H %P %at %an" --name-only --reverse
        // This gives us commits in fromBranch that aren't in toBranch
        
        /* PSEUDO CODE:
        var gitCommand = $"git log {toBranch}..{fromBranch} --pretty=format:\"==%H %P %at %an\" --name-only --reverse";
        var output = await ExecuteGitCommand(gitCommand);
        
        var commits = new Dictionary<string, Commit>();
        var childrenMap = new Dictionary<string, List<string>>();
        
        // Parse output line by line
        // Lines starting with "==" are commit info
        // Following lines until next "==" or blank line are modified files
        
        string currentSha = null;
        List<string> currentFiles = new();
        
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("=="))
            {
                // Parse: ==%H %P %at %an
                // %H = full commit hash
                // %P = parent hashes (space separated)
                // %at = author timestamp (unix)
                // %an = author name
                
                var parts = line.Substring(2).Split(' ', 4);
                var sha = parts[0];
                var parents = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2])).DateTime;
                var author = parts[3];
                
                // Get commit message
                var messageCommand = $"git log -1 --pretty=format:\"%s\" {sha}";
                var message = await ExecuteGitCommand(messageCommand);
                
                commits[sha] = new Commit { ... };
                
                // Build children map
                foreach (var parent in parents)
                {
                    if (!childrenMap.ContainsKey(parent))
                        childrenMap[parent] = new();
                    childrenMap[parent].Add(sha);
                }
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                currentFiles.Add(line);
            }
        }
        */
        
        throw new NotImplementedException("TODO: Implement ParseHistoryAsync");
    }
    
    public async Task<HashSet<string>> GetCommitsInBranchAsync(string branch)
    {
        // GIT COMMAND: git rev-list <branch>
        // Gets all commit SHAs reachable from branch
        
        /* PSEUDO CODE:
        var gitCommand = $"git rev-list {branch}";
        var output = await ExecuteGitCommand(gitCommand);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        */
        
        throw new NotImplementedException("TODO: Implement GetCommitsInBranchAsync");
    }
}

// ACCEPTANCE CRITERIA:
// - Parse all commits between branches correctly
// - Handle merge commits (multiple parents)
// - Extract file modifications for each commit
// - Build accurate parent-child relationship map
// - Handle large histories (1000+ commits) efficiently
```

### Task 1.3: Merge Commit Analyzer
**File**: `src/CherryPickSmart/Core/GitAnalysis/MergeCommitAnalyzer.cs`

```csharp
namespace CherryPickSmart.Core.GitAnalysis;

public class MergeCommitAnalyzer
{
    public record MergeAnalysis
    {
        public string MergeSha { get; init; } = "";
        public HashSet<string> IntroducedCommits { get; init; } = new();
        public bool IsCompleteInTarget { get; init; }
        public List<string> MissingCommits { get; init; } = new();
    }
    
    public List<MergeAnalysis> AnalyzeMerges(CommitGraph graph, HashSet<string> targetBranchCommits)
    {
        /* PSEUDO CODE:
        var mergeAnalyses = new List<MergeAnalysis>();
        
        // Find all merge commits
        var mergeCommits = graph.Commits.Values.Where(c => c.IsMergeCommit);
        
        foreach (var merge in mergeCommits)
        {
            // Get commits introduced by this merge
            // Formula: descendants(merge) - descendants(firstParent)
            
            var mergeDescendants = GetDescendants(graph, merge.Sha);
            var firstParentDescendants = merge.ParentShas.Any() 
                ? GetDescendants(graph, merge.ParentShas[0], firstParentOnly: true)
                : new HashSet<string>();
                
            var introducedCommits = mergeDescendants.Except(firstParentDescendants).ToHashSet();
            
            // Check if all introduced commits exist in target branch
            var missingCommits = introducedCommits.Except(targetBranchCommits).ToList();
            var isComplete = !missingCommits.Any();
            
            mergeAnalyses.Add(new MergeAnalysis
            {
                MergeSha = merge.Sha,
                IntroducedCommits = introducedCommits,
                IsCompleteInTarget = isComplete,
                MissingCommits = missingCommits
            });
        }
        
        return mergeAnalyses;
        */
        
        throw new NotImplementedException("TODO: Implement AnalyzeMerges");
    }
    
    private HashSet<string> GetDescendants(CommitGraph graph, string startSha, bool firstParentOnly = false)
    {
        /* PSEUDO CODE:
        var descendants = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(startSha);
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!descendants.Add(current))
                continue; // Already visited
                
            if (graph.ChildrenMap.TryGetValue(current, out var children))
            {
                if (firstParentOnly && children.Any())
                {
                    // For first-parent-only, we need to determine which child follows the first-parent line
                    // This requires checking which child has 'current' as its first parent
                    var firstParentChild = children.FirstOrDefault(childSha =>
                    {
                        var child = graph.Commits[childSha];
                        return child.ParentShas.FirstOrDefault() == current;
                    });
                    
                    if (firstParentChild != null)
                        queue.Enqueue(firstParentChild);
                }
                else
                {
                    foreach (var child in children)
                        queue.Enqueue(child);
                }
            }
        }
        
        return descendants;
        */
        
        throw new NotImplementedException("TODO: Implement GetDescendants");
    }
}

// ACCEPTANCE CRITERIA:
// - Correctly identify commits introduced by each merge
// - Accurately determine if merge is complete in target
// - Handle complex merge scenarios (octopus merges)
// - Performance: analyze 100 merges in < 1 second
```

---

## TASK 2: Ticket Extraction and Intelligence

### Task 2.1: Ticket Extractor
**File**: `src/CherryPickSmart/Core/TicketAnalysis/TicketExtractor.cs`

```csharp
namespace CherryPickSmart.Core.TicketAnalysis;

public class TicketExtractor
{
    private readonly List<TicketPattern> _patterns = new()
    {
        new(@"([A-Z]{2,10}-\d{1,6})", "JIRA_STANDARD"),        // HSAMED-1234
        new(@"(?i)(\w{2,10})[\s-](\d{1,6})", "FLEXIBLE"),     // hsamed 1234, hsamed-1234
        new(@"\[(\w{2,10})[\s-](\d{1,6})\]", "BRACKETED"),    // [hsamed 1234]
    };
    
    public record TicketPattern(string Regex, string Name);
    
    public List<string> ExtractTickets(string text)
    {
        /* PSEUDO CODE:
        var tickets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var pattern in _patterns)
        {
            var regex = new Regex(pattern.Regex, RegexOptions.IgnoreCase);
            var matches = regex.Matches(text);
            
            foreach (Match match in matches)
            {
                string ticketKey;
                
                if (pattern.Name == "JIRA_STANDARD")
                {
                    ticketKey = match.Groups[1].Value.ToUpper();
                }
                else if (pattern.Name == "FLEXIBLE")
                {
                    // Normalize: "hsamed 1234" -> "HSAMED-1234"
                    var prefix = match.Groups[1].Value.ToUpper();
                    var number = match.Groups[2].Value;
                    ticketKey = $"{prefix}-{number}";
                }
                else if (pattern.Name == "BRACKETED")
                {
                    // Normalize: "[hsamed 1234]" -> "HSAMED-1234"
                    var prefix = match.Groups[1].Value.ToUpper();
                    var number = match.Groups[2].Value;
                    ticketKey = $"{prefix}-{number}";
                }
                
                // Validate it's not a false positive (e.g., COVID-19)
                if (IsValidTicketPrefix(ticketKey))
                    tickets.Add(ticketKey);
            }
        }
        
        return tickets.OrderBy(t => t).ToList();
        */
        
        throw new NotImplementedException("TODO: Implement ExtractTickets");
    }
    
    public Dictionary<string, List<string>> BuildTicketCommitMap(CommitGraph graph)
    {
        /* PSEUDO CODE:
        var ticketToCommits = new Dictionary<string, List<string>>();
        
        foreach (var (sha, commit) in graph.Commits)
        {
            // Extract from commit message
            var messageTickets = ExtractTickets(commit.Message);
            
            // Also check branch name if available
            // GIT COMMAND: git name-rev --name-only <sha>
            // This might give us branch names like "feature/HSAMED-1234-fix-payment"
            
            foreach (var ticket in messageTickets)
            {
                if (!ticketToCommits.ContainsKey(ticket))
                    ticketToCommits[ticket] = new();
                    
                ticketToCommits[ticket].Add(sha);
                commit.ExtractedTickets.Add(ticket);
            }
        }
        
        return ticketToCommits;
        */
        
        throw new NotImplementedException("TODO: Implement BuildTicketCommitMap");
    }
}

// ACCEPTANCE CRITERIA:
// - Extract tickets from various formats
// - Handle case-insensitive matching
// - Normalize to standard format
// - No false positives (e.g., "COVID-19" is not a ticket)
```

### Task 2.2: Orphan Detection and Inference
**File**: `src/CherryPickSmart/Core/TicketAnalysis/OrphanCommitDetector.cs`

```csharp
namespace CherryPickSmart.Core.TicketAnalysis;

public class OrphanCommitDetector
{
    public record OrphanCommit
    {
        public Commit Commit { get; init; } = null!;
        public List<TicketSuggestion> Suggestions { get; init; } = new();
        public string Reason { get; init; } = ""; // Why it's orphaned
    }
    
    public record TicketSuggestion
    {
        public string TicketKey { get; init; } = "";
        public double Confidence { get; init; } // 0-100
        public List<string> Reasons { get; init; } = new();
    }
    
    public List<OrphanCommit> FindOrphans(
        CommitGraph graph, 
        Dictionary<string, List<string>> ticketCommitMap)
    {
        /* PSEUDO CODE:
        var orphans = new List<OrphanCommit>();
        var allCommitsWithTickets = ticketCommitMap.Values.SelectMany(x => x).ToHashSet();
        
        foreach (var (sha, commit) in graph.Commits)
        {
            if (!allCommitsWithTickets.Contains(sha))
            {
                var reason = DetermineOrphanReason(commit);
                orphans.Add(new OrphanCommit
                {
                    Commit = commit,
                    Reason = reason,
                    Suggestions = new() // Will be filled by inference engine
                });
            }
        }
        
        return orphans;
        */
        
        throw new NotImplementedException("TODO: Implement FindOrphans");
    }
    
    private string DetermineOrphanReason(Commit commit)
    {
        /* PSEUDO CODE:
        // Check for malformed tickets
        if (Regex.IsMatch(commit.Message, @"(?i)(hsamed|proj)\s*\d+"))
            return "Malformed ticket reference detected";
            
        if (commit.Message.Length < 10)
            return "Commit message too short";
            
        if (Regex.IsMatch(commit.Message, @"^(WIP|wip|temp|tmp)"))
            return "Work-in-progress commit";
            
        return "No ticket reference found";
        */
        
        throw new NotImplementedException();
    }
}

// ACCEPTANCE CRITERIA:
// - Identify all commits without tickets
// - Detect malformed ticket references
// - Provide clear reasons for orphan status
```

### Task 2.3: Ticket Inference Engine
**File**: `src/CherryPickSmart/Core/TicketAnalysis/TicketInferenceEngine.cs`

```csharp
namespace CherryPickSmart.Core.TicketAnalysis;

public class TicketInferenceEngine
{
    public async Task<List<TicketSuggestion>> GenerateSuggestionsAsync(
        OrphanCommit orphan,
        CommitGraph graph,
        Dictionary<string, List<string>> ticketCommitMap)
    {
        var suggestions = new List<TicketSuggestion>();
        
        // 1. Merge Context Analysis (highest confidence)
        suggestions.AddRange(await AnalyzeMergeContextAsync(orphan, graph, ticketCommitMap));
        
        // 2. Temporal Clustering (medium confidence)
        suggestions.AddRange(await AnalyzeTemporalClusteringAsync(orphan, graph, ticketCommitMap));
        
        // 3. File Overlap Analysis (medium confidence)
        suggestions.AddRange(await AnalyzeFileOverlapAsync(orphan, graph, ticketCommitMap));
        
        // 4. Aggregate and rank suggestions
        return RankSuggestions(suggestions);
    }
    
    private async Task<List<TicketSuggestion>> AnalyzeMergeContextAsync(
        OrphanCommit orphan,
        CommitGraph graph,
        Dictionary<string, List<string>> ticketCommitMap)
    {
        /* PSEUDO CODE:
        var suggestions = new List<TicketSuggestion>();
        
        // Find merge commits that contain this orphan
        // GIT COMMAND: git log --merges --ancestry-path <orphan-sha>..HEAD --format=%H
        
        foreach (var (mergeSha, merge) in graph.Commits.Where(c => c.Value.IsMergeCommit))
        {
            // Check if orphan is introduced by this merge
            var mergeAnalyzer = new MergeCommitAnalyzer();
            var analysis = mergeAnalyzer.AnalyzeMerges(graph, new HashSet<string>());
            
            var mergeAnalysis = analysis.FirstOrDefault(a => a.MergeSha == mergeSha);
            if (mergeAnalysis?.IntroducedCommits.Contains(orphan.Commit.Sha) == true)
            {
                // Find dominant ticket in this merge's introduced commits
                var ticketCounts = new Dictionary<string, int>();
                
                foreach (var introducedSha in mergeAnalysis.IntroducedCommits)
                {
                    if (graph.Commits[introducedSha].ExtractedTickets.Any())
                    {
                        foreach (var ticket in graph.Commits[introducedSha].ExtractedTickets)
                        {
                            ticketCounts[ticket] = ticketCounts.GetValueOrDefault(ticket) + 1;
                        }
                    }
                }
                
                if (ticketCounts.Any())
                {
                    var dominantTicket = ticketCounts.OrderByDescending(x => x.Value).First();
                    var totalCommitsInMerge = mergeAnalysis.IntroducedCommits.Count;
                    var confidence = (dominantTicket.Value / (double)totalCommitsInMerge) * 100;
                    
                    suggestions.Add(new TicketSuggestion
                    {
                        TicketKey = dominantTicket.Key,
                        Confidence = Math.Min(95, confidence + 20), // Boost for merge context
                        Reasons = new() { "merge_context", $"part_of_merge_{merge.ShortSha}" }
                    });
                }
            }
        }
        
        return suggestions;
        */
        
        throw new NotImplementedException("TODO: Implement AnalyzeMergeContextAsync");
    }
    
    private async Task<List<TicketSuggestion>> AnalyzeTemporalClusteringAsync(
        OrphanCommit orphan,
        CommitGraph graph,
        Dictionary<string, List<string>> ticketCommitMap)
    {
        /* PSEUDO CODE:
        var suggestions = new List<TicketSuggestion>();
        var timeWindow = TimeSpan.FromHours(4); // Commits within 4 hours
        
        // Find commits by same author within time window
        var nearbyCommits = graph.Commits.Values
            .Where(c => c.Author == orphan.Commit.Author)
            .Where(c => Math.Abs((c.Timestamp - orphan.Commit.Timestamp).TotalHours) <= timeWindow.TotalHours)
            .Where(c => c.ExtractedTickets.Any())
            .ToList();
            
        // Weight by temporal proximity
        var ticketScores = new Dictionary<string, double>();
        
        foreach (var nearby in nearbyCommits)
        {
            var timeDiff = Math.Abs((nearby.Timestamp - orphan.Commit.Timestamp).TotalMinutes);
            var weight = 1.0 / (1.0 + timeDiff / 60.0); // Decay function
            
            foreach (var ticket in nearby.ExtractedTickets)
            {
                ticketScores[ticket] = ticketScores.GetValueOrDefault(ticket) + weight;
            }
        }
        
        // Convert scores to confidence (0-100)
        foreach (var (ticket, score) in ticketScores.OrderByDescending(x => x.Value).Take(3))
        {
            var confidence = Math.Min(70, score * 40); // Cap at 70% for temporal
            suggestions.Add(new TicketSuggestion
            {
                TicketKey = ticket,
                Confidence = confidence,
                Reasons = new() { "temporal_clustering", $"by_{orphan.Commit.Author}" }
            });
        }
        
        return suggestions;
        */
        
        throw new NotImplementedException("TODO: Implement AnalyzeTemporalClusteringAsync");
    }
    
    private async Task<List<TicketSuggestion>> AnalyzeFileOverlapAsync(
        OrphanCommit orphan,
        CommitGraph graph,
        Dictionary<string, List<string>> ticketCommitMap)
    {
        /* PSEUDO CODE:
        var suggestions = new List<TicketSuggestion>();
        
        // Find commits that modify same files
        var fileOverlaps = new Dictionary<string, List<string>>(); // ticket -> overlapping files
        
        foreach (var (ticket, commitShas) in ticketCommitMap)
        {
            var overlappingFiles = new HashSet<string>();
            
            foreach (var sha in commitShas)
            {
                if (graph.Commits.TryGetValue(sha, out var commit))
                {
                    var commonFiles = commit.ModifiedFiles.Intersect(orphan.Commit.ModifiedFiles);
                    overlappingFiles.UnionWith(commonFiles);
                }
            }
            
            if (overlappingFiles.Any())
            {
                fileOverlaps[ticket] = overlappingFiles.ToList();
            }
        }
        
        // Calculate confidence based on overlap percentage
        foreach (var (ticket, overlappingFiles) in fileOverlaps.OrderByDescending(x => x.Value.Count).Take(3))
        {
            var overlapRatio = overlappingFiles.Count / (double)orphan.Commit.ModifiedFiles.Count;
            var confidence = Math.Min(60, overlapRatio * 80); // Cap at 60% for file overlap
            
            suggestions.Add(new TicketSuggestion
            {
                TicketKey = ticket,
                Confidence = confidence,
                Reasons = new() { "file_overlap", $"{overlappingFiles.Count}_common_files" }
            });
        }
        
        return suggestions;
        */
        
        throw new NotImplementedException("TODO: Implement AnalyzeFileOverlapAsync");
    }
    
    private List<TicketSuggestion> RankSuggestions(List<TicketSuggestion> suggestions)
    {
        /* PSEUDO CODE:
        // Aggregate suggestions by ticket, taking highest confidence
        var aggregated = suggestions
            .GroupBy(s => s.TicketKey)
            .Select(g => new TicketSuggestion
            {
                TicketKey = g.Key,
                Confidence = g.Max(s => s.Confidence),
                Reasons = g.SelectMany(s => s.Reasons).Distinct().ToList()
            })
            .OrderByDescending(s => s.Confidence)
            .ToList();
            
        return aggregated;
        */
        
        throw new NotImplementedException();
    }
}

// ACCEPTANCE CRITERIA:
// - Generate suggestions with confidence scores
// - Merge context suggestions > 80% confidence
// - Temporal clustering 40-70% confidence
// - File overlap 30-60% confidence
// - No duplicate suggestions
```

---

## TASK 3: CLI Commands Implementation

### Task 3.1: Main Program Entry
**File**: `src/CherryPickSmart/Program.cs`

```csharp
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CherryPickSmart;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        
        return await Parser.Default.ParseArguments
            AnalyzeCommand,
            PlanCommand,
            ExecuteCommand,
            ConfigCommand>(args)
            .MapResult(
                async (AnalyzeCommand cmd) => await RunCommandAsync(host, cmd),
                async (PlanCommand cmd) => await RunCommandAsync(host, cmd),
                async (ExecuteCommand cmd) => await RunCommandAsync(host, cmd),
                async (ConfigCommand cmd) => await RunCommandAsync(host, cmd),
                errs => Task.FromResult(1));
    }
    
    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Register all services
                services.AddSingleton<GitHistoryParser>();
                services.AddSingleton<MergeCommitAnalyzer>();
                services.AddSingleton<TicketExtractor>();
                services.AddSingleton<OrphanCommitDetector>();
                services.AddSingleton<TicketInferenceEngine>();
                services.AddSingleton<JiraClient>();
                services.AddSingleton<ConfigurationService>();
                services.AddSingleton<GitCommandExecutor>();
            });
}
```

### Task 3.2: Analyze Command
**File**: `src/CherryPickSmart/Commands/AnalyzeCommand.cs`

```csharp
namespace CherryPickSmart.Commands;

[Verb("analyze", HelpText = "Analyze commits between branches")]
public class AnalyzeCommand : ICommand
{
    [Option('f', "from", Required = true, HelpText = "Source branch (e.g., deploy/dev)")]
    public string FromBranch { get; set; } = "";
    
    [Option('t', "to", Required = true, HelpText = "Target branch (e.g., deploy/uat)")]
    public string ToBranch { get; set; } = "";
    
    [Option('o', "orphans", HelpText = "Show detailed orphan analysis")]
    public bool ShowOrphans { get; set; }
    
    public async Task<int> ExecuteAsync(IServiceProvider services)
    {
        var parser = services.GetRequiredService<GitHistoryParser>();
        var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
        var ticketExtractor = services.GetRequiredService<TicketExtractor>();
        
        /* PSEUDO CODE:
        // 1. Parse git history
        Console.WriteLine($"Analyzing commits from {FromBranch} to {ToBranch}...");
        var graph = await parser.ParseHistoryAsync(FromBranch, ToBranch);
        
        // 2. Get commits already in target branch
        var targetCommits = await parser.GetCommitsInBranchAsync(ToBranch);
        
        // 3. Analyze commits
        Console.WriteLine($"\nCommit Analysis:");
        Console.WriteLine($"  Total commits to cherry-pick: {graph.Commits.Count}");
        Console.WriteLine($"  Regular commits: {graph.Commits.Count(c => !c.Value.IsMergeCommit)}");
        Console.WriteLine($"  Merge commits: {graph.Commits.Count(c => c.Value.IsMergeCommit)}");
        
        // 4. Analyze merges
        var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);
        var completeMerges = mergeAnalyses.Where(m => m.IsCompleteInTarget).ToList();
        
        Console.WriteLine($"\nMerge Analysis:");
        Console.WriteLine($"  Complete merges (can be preserved): {completeMerges.Count}");
        Console.WriteLine($"  Incomplete merges (need cherry-picking): {mergeAnalyses.Count - completeMerges.Count}");
        
        // 5. Extract tickets
        var ticketMap = ticketExtractor.BuildTicketCommitMap(graph);
        Console.WriteLine($"\nTicket Analysis:");
        Console.WriteLine($"  Tickets found: {ticketMap.Count}");
        
        foreach (var (ticket, commits) in ticketMap.OrderBy(t => t.Key))
        {
            Console.WriteLine($"    {ticket}: {commits.Count} commits");
        }
        
        // 6. Find orphans if requested
        if (ShowOrphans)
        {
            var orphanDetector = services.GetRequiredService<OrphanCommitDetector>();
            var orphans = orphanDetector.FindOrphans(graph, ticketMap);
            
            Console.WriteLine($"\nOrphaned Commits: {orphans.Count}");
            foreach (var orphan in orphans)
            {
                Console.WriteLine($"  {orphan.Commit.ShortSha}: {orphan.Commit.Message.Truncate(50)}");
                Console.WriteLine($"    Reason: {orphan.Reason}");
            }
        }
        
        return 0;
        */
        
        throw new NotImplementedException("TODO: Implement AnalyzeCommand");
    }
}

// USAGE: cps analyze --from deploy/dev --to deploy/uat --orphans
```

### Task 3.3: Plan Command (Interactive)
**File**: `src/CherryPickSmart/Commands/PlanCommand.cs`

```csharp
namespace CherryPickSmart.Commands;

[Verb("plan", HelpText = "Create interactive cherry-pick plan")]
public class PlanCommand : ICommand
{
    [Option('f', "from", Required = true)]
    public string FromBranch { get; set; } = "";
    
    [Option('t', "to", Required = true)]
    public string ToBranch { get; set; } = "";
    
    [Option("auto-infer", HelpText = "Automatically accept high-confidence inferences")]
    public bool AutoInfer { get; set; }
    
    [Option("output", HelpText = "Save plan to file")]
    public string? OutputFile { get; set; }
    
    public async Task<int> ExecuteAsync(IServiceProvider services)
    {
        /* PSEUDO CODE:
        // 1. Run full analysis
        var parser = services.GetRequiredService<GitHistoryParser>();
        var graph = await parser.ParseHistoryAsync(FromBranch, ToBranch);
        var targetCommits = await parser.GetCommitsInBranchAsync(ToBranch);
        
        // 2. Extract tickets and get Jira info
        var ticketExtractor = services.GetRequiredService<TicketExtractor>();
        var ticketMap = ticketExtractor.BuildTicketCommitMap(graph);
        
        var jiraClient = services.GetRequiredService<JiraClient>();
        var ticketInfos = await jiraClient.GetTicketsBatchAsync(ticketMap.Keys.ToList());
        
        // 3. Find and resolve orphans
        var orphanDetector = services.GetRequiredService<OrphanCommitDetector>();
        var inferenceEngine = services.GetRequiredService<TicketInferenceEngine>();
        var orphans = orphanDetector.FindOrphans(graph, ticketMap);
        
        // Generate suggestions for orphans
        foreach (var orphan in orphans)
        {
            var suggestions = await inferenceEngine.GenerateSuggestionsAsync(orphan, graph, ticketMap);
            orphan.Suggestions.AddRange(suggestions);
        }
        
        // 4. Interactive resolution
        var promptService = services.GetRequiredService<InteractivePromptService>();
        var orphanAssignments = await promptService.ResolveOrphansAsync(orphans, AutoInfer);
        
        // 5. Select tickets
        var selectedTickets = promptService.SelectTickets(ticketInfos);
        
        // 6. Analyze conflicts and optimize order
        var selectedCommits = GetCommitsForTickets(selectedTickets, ticketMap, orphanAssignments);
        var conflictPredictor = services.GetRequiredService<ConflictPredictor>();
        var conflicts = conflictPredictor.PredictConflicts(selectedCommits, targetCommits);
        
        if (conflicts.Any(c => c.Risk >= ConflictRisk.High))
        {
            AnsiConsole.MarkupLine("[red]Warning: High risk conflicts detected![/]");
            // Show conflict details
        }
        
        // 7. Generate optimized cherry-pick plan
        var optimizer = services.GetRequiredService<OrderOptimizer>();
        var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
        var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);
        
        var plan = optimizer.OptimizeOrder(selectedCommits, mergeAnalyses, conflicts);
        
        // 8. Display plan
        DisplayCherryPickPlan(plan);
        
        // 9. Save plan if requested
        if (!string.IsNullOrEmpty(OutputFile))
        {
            var planJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(OutputFile, planJson);
            Console.WriteLine($"\nPlan saved to: {OutputFile}");
        }
        
        return 0;
        */
        
        throw new NotImplementedException("TODO: Implement PlanCommand");
    }
}

// USAGE: cps plan --from deploy/dev --to deploy/uat --output plan.json
```

---

## TASK 4: Conflict Analysis and Order Optimization

### Task 4.1: Conflict Predictor
**File**: `src/CherryPickSmart/Core/ConflictAnalysis/ConflictPredictor.cs`

```csharp
namespace CherryPickSmart.Core.ConflictAnalysis;

public class ConflictPredictor
{
    public record ConflictPrediction
    {
        public string File { get; init; } = "";
        public List<Commit> ConflictingCommits { get; init; } = new();
        public ConflictRisk Risk { get; init; }
        public string Description { get; init; } = "";
    }
    
    public enum ConflictRisk { Low, Medium, High, Certain }
    
    public List<ConflictPrediction> PredictConflicts(
        List<Commit> commitsToCherry,
        HashSet<string> targetBranchCommits)
    {
        /* PSEUDO CODE:
        var predictions = new List<ConflictPrediction>();
        
        // 1. Group commits by file
        var fileCommitMap = new Dictionary<string, List<Commit>>();
        foreach (var commit in commitsToCherry)
        {
            foreach (var file in commit.ModifiedFiles)
            {
                if (!fileCommitMap.ContainsKey(file))
                    fileCommitMap[file] = new();
                fileCommitMap[file].Add(commit);
            }
        }
        
        // 2. Analyze each file with multiple commits
        foreach (var (file, commits) in fileCommitMap.Where(f => f.Value.Count > 1))
        {
            var risk = CalculateConflictRisk(file, commits, targetBranchCommits);
            
            if (risk > ConflictRisk.Low)
            {
                predictions.Add(new ConflictPrediction
                {
                    File = file,
                    ConflictingCommits = commits,
                    Risk = risk,
                    Description = GenerateRiskDescription(file, commits, risk)
                });
            }
        }
        
        // 3. Check for files modified in target branch
        // GIT COMMAND: git diff --name-only <toBranch>...<fromBranch>
        // This shows files that differ between branches
        
        return predictions.OrderByDescending(p => p.Risk).ToList();
        */
        
        throw new NotImplementedException("TODO: Implement PredictConflicts");
    }
    
    private ConflictRisk CalculateConflictRisk(
        string file, 
        List<Commit> commits, 
        HashSet<string> targetBranchCommits)
    {
        /* PSEUDO CODE:
        // Factors that increase risk:
        // 1. Commits are far apart in time (more likely to have diverged)
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);
        var timeFactor = timeSpan.TotalDays > 7 ? 2 : 1;
        
        // 2. Different authors (less coordination)
        var authorCount = commits.Select(c => c.Author).Distinct().Count();
        var authorFactor = authorCount > 1 ? 2 : 1;
        
        // 3. File was modified in target branch since branch point
        // GIT COMMAND: git log -1 --format=%H <toBranch> -- <file>
        var modifiedInTarget = CheckFileModifiedInTarget(file, targetBranchCommits);
        var targetFactor = modifiedInTarget ? 3 : 1;
        
        var totalRisk = timeFactor * authorFactor * targetFactor;
        
        return totalRisk switch
        {
            >= 6 => ConflictRisk.High,
            >= 4 => ConflictRisk.Medium,
            _ => ConflictRisk.Low
        };
        */
        
        throw new NotImplementedException();
    }
}

// ACCEPTANCE CRITERIA:
// - Accurately predict file-level conflicts
// - Risk assessment based on multiple factors
// - Clear descriptions for each prediction
```

### Task 4.2: Cherry-Pick Order Optimizer
**File**: `src/CherryPickSmart/Core/ConflictAnalysis/OrderOptimizer.cs`

```csharp
namespace CherryPickSmart.Core.ConflictAnalysis;

public class OrderOptimizer
{
    public List<CherryPickStep> OptimizeOrder(
        List<Commit> selectedCommits,
        List<MergeAnalysis> completeMerges,
        List<ConflictPrediction> conflicts)
    {
        /* PSEUDO CODE:
        var steps = new List<CherryPickStep>();
        var processedCommits = new HashSet<string>();
        
        // 1. First, add complete merges that can be preserved
        foreach (var merge in completeMerges)
        {
            var mergeCommitShas = merge.IntroducedCommits
                .Where(sha => selectedCommits.Any(c => c.Sha == sha))
                .ToList();
                
            if (mergeCommitShas.Any())
            {
                steps.Add(new CherryPickStep
                {
                    Type = StepType.MergeCommit,
                    CommitShas = new List<string> { merge.MergeSha },
                    Description = $"Preserve merge {merge.MergeSha[..8]} with {mergeCommitShas.Count} commits",
                    GitCommand = $"git cherry-pick -m 1 {merge.MergeSha}"
                });
                
                processedCommits.UnionWith(mergeCommitShas);
            }
        }
        
        // 2. Group remaining commits by ticket
        var ticketGroups = selectedCommits
            .Where(c => !processedCommits.Contains(c.Sha))
            .GroupBy(c => c.ExtractedTickets.FirstOrDefault() ?? c.InferredTicket ?? "NO_TICKET")
            .ToList();
            
        // 3. Order ticket groups to minimize conflicts
        var orderedGroups = OrderTicketsByDependencyAndConflict(ticketGroups, conflicts);
        
        // 4. For each ticket group, order commits chronologically
        foreach (var group in orderedGroups)
        {
            var ticketCommits = group.OrderBy(c => c.Timestamp).ToList();
            
            // Try to find ranges of consecutive commits
            var ranges = FindConsecutiveRanges(ticketCommits);
            
            foreach (var range in ranges)
            {
                if (range.Count == 1)
                {
                    steps.Add(new CherryPickStep
                    {
                        Type = StepType.SingleCommit,
                        CommitShas = new List<string> { range[0].Sha },
                        Description = $"{group.Key}: {range[0].Message.Truncate(50)}",
                        GitCommand = $"git cherry-pick {range[0].Sha}"
                    });
                }
                else
                {
                    steps.Add(new CherryPickStep
                    {
                        Type = StepType.CommitRange,
                        CommitShas = range.Select(c => c.Sha).ToList(),
                        Description = $"{group.Key}: {range.Count} commits",
                        GitCommand = $"git cherry-pick {range.First().Sha}^..{range.Last().Sha}"
                    });
                }
            }
        }
        
        return steps;
        */
        
        throw new NotImplementedException("TODO: Implement OptimizeOrder");
    }
}

public record CherryPickStep
{
    public StepType Type { get; init; }
    public List<string> CommitShas { get; init; } = new();
    public string Description { get; init; } = "";
    public string GitCommand { get; init; } = "";
}

public enum StepType { SingleCommit, MergeCommit, CommitRange }

// ACCEPTANCE CRITERIA:
// - Preserve complete merges when possible
// - Respect ticket dependencies
// - Minimize predicted conflicts
// - Generate executable git commands
```

---

## TASK 5: Interactive CLI Experience

### Task 5.1: Interactive Prompt Service
**File**: `src/CherryPickSmart/Services/InteractivePromptService.cs`

```csharp
using Spectre.Console;

namespace CherryPickSmart.Services;

public class InteractivePromptService
{
    public List<string> SelectTickets(Dictionary<string, TicketInfo> availableTickets)
    {
        /* PSEUDO CODE:
        // Create a tree structure for better visualization
        var tree = new Tree("[yellow]Available Tickets[/]");
        
        // Group by status
        var byStatus = availableTickets.GroupBy(t => t.Value.Status);
        
        foreach (var statusGroup in byStatus)
        {
            var statusNode = tree.AddNode($"[blue]{statusGroup.Key}[/] ({statusGroup.Count()})");
            
            foreach (var (key, info) in statusGroup)
            {
                var markup = $"[green]{key}[/] - {info.Summary.EscapeMarkup()}";
                if (info.Priority == "High")
                    markup = $"[red]![/] {markup}";
                    
                statusNode.AddNode(markup);
            }
        }
        
        AnsiConsole.Write(tree);
        
        // Multi-select prompt
        var choices = availableTickets.Select(t => 
        {
            var display = $"{t.Key} - {t.Value.Summary}";
            if (t.Value.Status != "Ready for Deployment")
                display += $" [{t.Value.Status}]";
            return display;
        }).ToList();
        
        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select tickets to cherry-pick:")
                .PageSize(15)
                .MoreChoicesText("[grey](Move up and down to reveal more tickets)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoices(choices));
                
        return selected.Select(s => s.Split(" - ")[0]).ToList();
        */
        
        throw new NotImplementedException("TODO: Implement SelectTickets");
    }
    
    public async Task<Dictionary<string, string>> ResolveOrphansAsync(
        List<OrphanCommit> orphans,
        bool autoAcceptHighConfidence = false)
    {
        /* PSEUDO CODE:
        var assignments = new Dictionary<string, string>();
        
        AnsiConsole.Rule("[red]Orphaned Commits[/]");
        AnsiConsole.MarkupLine($"Found [yellow]{orphans.Count}[/] commits without ticket references.\n");
        
        foreach (var orphan in orphans)
        {
            // Create panel with commit info
            var panel = new Panel(
                $"[yellow]SHA:[/] {orphan.Commit.ShortSha}\n" +
                $"[yellow]Author:[/] {orphan.Commit.Author}\n" +
                $"[yellow]Date:[/] {orphan.Commit.Timestamp:yyyy-MM-dd HH:mm}\n" +
                $"[yellow]Message:[/] {orphan.Commit.Message}\n" +
                $"[yellow]Files:[/] {string.Join(", ", orphan.Commit.ModifiedFiles.Take(3))}...")
                .Header($"[red]Orphaned Commit[/]")
                .BorderColor(Color.Red);
                
            AnsiConsole.Write(panel);
            
            // Check for auto-accept
            var highConfidenceSuggestion = orphan.Suggestions.FirstOrDefault(s => s.Confidence >= 80);
            
            if (autoAcceptHighConfidence && highConfidenceSuggestion != null)
            {
                assignments[orphan.Commit.Sha] = highConfidenceSuggestion.TicketKey;
                AnsiConsole.MarkupLine(
                    $"[green]✓ Auto-assigned to {highConfidenceSuggestion.TicketKey}[/] " +
                    $"({highConfidenceSuggestion.Confidence:F0}% confidence - {string.Join(", ", highConfidenceSuggestion.Reasons)})");
                continue;
            }
            
            // Show suggestions table
            if (orphan.Suggestions.Any())
            {
                var table = new Table();
                table.AddColumn("Ticket");
                table.AddColumn("Confidence");
                table.AddColumn("Reasons");
                
                foreach (var suggestion in orphan.Suggestions.Take(5))
                {
                    var confidenceColor = suggestion.Confidence >= 70 ? "green" : 
                                         suggestion.Confidence >= 40 ? "yellow" : "red";
                    
                    table.AddRow(
                        suggestion.TicketKey,
                        $"[{confidenceColor}]{suggestion.Confidence:F0}%[/]",
                        string.Join(", ", suggestion.Reasons));
                }
                
                AnsiConsole.Write(table);
            }
            
            // Prompt for action
            var choices = new List<string> { "Skip this commit" };
            if (orphan.Suggestions.Any())
                choices.AddRange(orphan.Suggestions.Select(s => s.TicketKey));
            choices.Add("Enter ticket manually");
            
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select ticket for this commit:")
                    .AddChoices(choices));
                    
            if (choice == "Enter ticket manually")
            {
                var ticket = AnsiConsole.Ask<string>("Enter ticket (e.g., HSAMED-1234):");
                assignments[orphan.Commit.Sha] = ticket;
            }
            else if (choice != "Skip this commit")
            {
                assignments[orphan.Commit.Sha] = choice;
            }
            
            AnsiConsole.WriteLine();
        }
        
        return assignments;
        */
        
        throw new NotImplementedException("TODO: Implement ResolveOrphansAsync");
    }
}

// ACCEPTANCE CRITERIA:
// - Clean, intuitive interface using Spectre.Console
// - Multi-select for tickets
// - Clear orphan resolution flow
// - Progress indicators for long operations
```

---

## TASK 6: Configuration and Jira Integration

### Task 6.1: Configuration Service
**File**: `src/CherryPickSmart/Services/ConfigurationService.cs`

```csharp
namespace CherryPickSmart.Services;

public class ConfigurationService
{
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cherry-pick-smart",
        "config.json");
        
    public record Config
    {
        public string? JiraUrl { get; init; }
        public string? JiraUsername { get; init; }
        public string? JiraApiToken { get; init; }
        public List<string> TicketPrefixes { get; init; } = new() { "HSAMED" };
        public string DefaultFromBranch { get; init; } = "deploy/dev";
        public string DefaultToBranch { get; init; } = "deploy/uat";
    }
    
    public async Task<Config> LoadConfigAsync()
    {
        if (!File.Exists(_configPath))
            return new Config();
            
        var json = await File.ReadAllTextAsync(_configPath);
        return JsonSerializer.Deserialize<Config>(json) ?? new Config();
    }
    
    public async Task SaveConfigAsync(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, json);
    }
}
```

### Task 6.2: Jira Client
**File**: `src/CherryPickSmart/Core/Integration/JiraClient.cs`

```csharp
namespace CherryPickSmart.Core.Integration;

public class JiraClient
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _config;
    private readonly Dictionary<string, JiraTicket> _cache = new();
    
    public record JiraTicket
    {
        public string Key { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Assignee { get; init; }
        public string Priority { get; init; } = "";
        public List<string> Labels { get; init; } = new();
    }
    
    public async Task<JiraTicket?> GetTicketAsync(string ticketKey)
    {
        /* PSEUDO CODE:
        // Check cache
        if (_cache.TryGetValue(ticketKey, out var cached))
            return cached;
            
        // Jira REST API call
        var config = await _config.LoadConfigAsync();
        if (string.IsNullOrEmpty(config.JiraUrl))
            return null;
            
        var url = $"{config.JiraUrl}/rest/api/2/issue/{ticketKey}";
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var authToken = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{config.JiraUsername}:{config.JiraApiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        
        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;
                
            var json = await response.Content.ReadAsStringAsync();
            var jiraResponse = JsonSerializer.Deserialize<JiraApiResponse>(json);
            
            var ticket = new JiraTicket
            {
                Key = jiraResponse.Key,
                Summary = jiraResponse.Fields.Summary,
                Status = jiraResponse.Fields.Status.Name,
                Assignee = jiraResponse.Fields.Assignee?.DisplayName,
                Priority = jiraResponse.Fields.Priority?.Name ?? "Medium",
                Labels = jiraResponse.Fields.Labels ?? new()
            };
            
            _cache[ticketKey] = ticket;
            return ticket;
        }
        catch (Exception ex)
        {
            // Log error
            return null;
        }
        */
        
        throw new NotImplementedException("TODO: Implement GetTicketAsync");
    }
    
    public async Task<Dictionary<string, JiraTicket>> GetTicketsBatchAsync(List<string> ticketKeys)
    {
        /* PSEUDO CODE:
        var results = new Dictionary<string, JiraTicket>();
        
        // Check cache first
        var uncachedKeys = ticketKeys.Where(k => !_cache.ContainsKey(k)).ToList();
        foreach (var key in ticketKeys.Where(k => _cache.ContainsKey(k)))
        {
            results[key] = _cache[key];
        }
        
        if (!uncachedKeys.Any())
            return results;
            
        // Batch API using JQL
        var config = await _config.LoadConfigAsync();
        if (string.IsNullOrEmpty(config.JiraUrl))
            return results;
            
        // Jira limits JQL IN queries, so batch in groups of 50
        foreach (var batch in uncachedKeys.Chunk(50))
        {
            var jql = $"key in ({string.Join(",", batch)})";
            var url = $"{config.JiraUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields=summary,status,assignee,priority,labels";
            
            // Make request similar to GetTicketAsync
            // Parse response and add to results + cache
        }
        
        return results;
        */
        
        throw new NotImplementedException("TODO: Implement GetTicketsBatchAsync");
    }
}

// ACCEPTANCE CRITERIA:
// - Efficient batch fetching
// - Proper error handling
// - Rate limit respect
// - Cache to minimize API calls
```

### Task 6.3: Git Command Executor
**File**: `src/CherryPickSmart/Core/Integration/GitCommandExecutor.cs`

```csharp
namespace CherryPickSmart.Core.Integration;

public class GitCommandExecutor
{
    private readonly ILogger<GitCommandExecutor> _logger;
    
    public async Task<string> ExecuteAsync(string command)
    {
        /* PSEUDO CODE:
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        var output = new StringBuilder();
        var error = new StringBuilder();
        
        process.OutputDataReceived += (sender, e) => 
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };
        
        process.ErrorDataReceived += (sender, e) => 
        {
            if (e.Data != null) error.AppendLine(e.Data);
        };
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"Git command failed: {command}\nError: {error}");
        }
        
        return output.ToString();
        */
        
        throw new NotImplementedException("TODO: Implement ExecuteAsync");
    }
    
    public async Task<bool> IsGitRepositoryAsync()
    {
        /* PSEUDO CODE:
        try
        {
            await ExecuteAsync("rev-parse --git-dir");
            return true;
        }
        catch
        {
            return false;
        }
        */
        
        throw new NotImplementedException();
    }
}
```

---

## PROJECT-WIDE ACCEPTANCE CRITERIA

1. **Performance**
   - Analyze 1000+ commits in < 5 seconds
   - Handle large file histories efficiently
   - Minimal memory footprint

2. **Accuracy**
   - 95%+ accuracy in ticket extraction
   - 80%+ accuracy in high-confidence inferences
   - Zero false positives in conflict prediction

3. **Usability**
   - Clear, actionable error messages
   - Progress indication for long operations
   - Helpful suggestions for common issues
   - --help on all commands with examples

4. **Safety**
   - Never execute git commands without confirmation
   - Dry-run mode for all operations
   - Clear warnings for risky operations
   - Ability to save and review plans before execution

---

## INITIAL TEST SCENARIOS

### Scenario 1: Simple Cherry-Pick
```bash
# Given: dev has 5 commits for HSAMED-1234
# When: User runs analyze
cps analyze --from deploy/dev --to deploy/uat

# Then: Shows 5 commits, 1 ticket, 0 orphans
```

### Scenario 2: Orphan Resolution
```bash
# Given: dev has commits without ticket refs
# When: User runs plan
cps plan --from deploy/dev --to deploy/uat

# Then: Prompts for orphan assignment with suggestions
```

### Scenario 3: Complete Merge Preservation
```bash
# Given: Merge commit with all children already in UAT
# When: User creates plan
cps plan --from deploy/dev --to deploy/uat

# Then: Suggests cherry-picking merge commit directly
```

---

## DEVELOPMENT PHASES

### Phase 1: Core Analysis (Week 1)
- [ ] Task 1.1: Basic Models
- [ ] Task 1.2: Git History Parser
- [ ] Task 1.3: Merge Analyzer
- [ ] Task 2.1: Ticket Extractor

### Phase 2: Intelligence (Week 2)
- [ ] Task 2.2: Orphan Detection
- [ ] Task 2.3: Inference Engine
- [ ] Task 4.1: Conflict Predictor

### Phase 3: CLI & Integration (Week 3)
- [ ] Task 3.1: Main Program
- [ ] Task 3.2: Analyze Command
- [ ] Task 3.3: Plan Command
- [ ] Task 5.1: Interactive Service

### Phase 4: Polish & Testing (Week 4)
- [ ] Task 6.1: Configuration
- [ ] Task 6.2: Jira Integration
- [ ] Task 4.2: Order Optimizer
- [ ] End-to-end testing

---

## VS CODE COPILOT USAGE NOTES

1. **Use this PRD as context** - Keep this file open when implementing
2. **Reference acceptance criteria** - Each task has specific criteria
3. **Follow naming conventions** - All names are specified
4. **Implement in order** - Tasks build on each other
5. **Test as you go** - Test scenarios are provided

### Quick Start for Copilot
```bash
# Create project
dotnet new console -n CherryPickSmart
cd CherryPickSmart

# Add packages
dotnet add package CommandLineParser
dotnet add package Spectre.Console
dotnet add package Microsoft.Extensions.Hosting
dotnet add package LibGit2Sharp
dotnet add package RestSharp

# Create folder structure
mkdir -p Commands Core/GitAnalysis Core/TicketAnalysis Core/ConflictAnalysis Core/Integration Models Services

# Start with Task 1.1 - Create Models/Commit.cs
```