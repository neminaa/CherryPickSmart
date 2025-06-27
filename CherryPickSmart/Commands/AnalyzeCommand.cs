using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.TicketAnalysis;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;

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

    [Option('r', "repo", Required = true, HelpText = "Path to the repository")]
    public string RepositoryPath { get; set; } = "";

    public Task<int> ExecuteAsync(IServiceProvider services)
    {
        var parser = services.GetRequiredService<GitHistoryParser>();
        var mergeAnalyzer = services.GetRequiredService<MergeCommitAnalyzer>();
        var ticketExtractor = services.GetRequiredService<TicketExtractor>();

        Console.WriteLine($"Analyzing commits from {FromBranch} to {ToBranch} in repository {RepositoryPath}...");
        var graph = parser.ParseHistory(RepositoryPath, FromBranch, ToBranch);

        var targetCommits = parser.GetCommitsInBranch(RepositoryPath, ToBranch);

        Console.WriteLine($"\nCommit Analysis:");
        Console.WriteLine($"  Total commits to cherry-pick: {graph.Commits.Count}");
        Console.WriteLine($"  Regular commits: {graph.Commits.Count(c => !c.Value.IsMergeCommit)}");
        Console.WriteLine($"  Merge commits: {graph.Commits.Count(c => c.Value.IsMergeCommit)}");

        var mergeAnalyses = mergeAnalyzer.AnalyzeMerges(graph, targetCommits);
        var completeMerges = mergeAnalyses.Where(m => m.IsCompleteInTarget).ToList();

        Console.WriteLine($"\nMerge Analysis:");
        Console.WriteLine($"  Complete merges (can be preserved): {completeMerges.Count}");
        Console.WriteLine($"  Incomplete merges (need cherry-picking): {mergeAnalyses.Count - completeMerges.Count}");

        var ticketMap = ticketExtractor.BuildTicketCommitMap(graph);
        Console.WriteLine($"\nTicket Analysis:");
        Console.WriteLine($"  Tickets found: {ticketMap.Count}");

        foreach (var (ticket, commits) in ticketMap.OrderBy(t => t.Key))
        {
            Console.WriteLine($"    {ticket}: {commits.Count} commits");
        }

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

        return Task.FromResult(0);
    }
}

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "...");
    }
}
