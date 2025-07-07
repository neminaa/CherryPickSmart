using CommandLine;

namespace CherryPickAnalyzer.Options;

[Verb("analyze", HelpText = "Analyze deployment differences between branches")]
public class AnalyzeOptions
{
    [Option('r', "repo", Required = false, HelpText = "Path to git repository", Default = ".")]
    public string RepoPath { get; set; } = ".";

    [Option('s', "source", Required = true, HelpText = "Source branch (contains new changes)")]
    public string SourceBranch { get; set; } = "";

    [Option('t', "target", Required = true, HelpText = "Target branch (deployment target)")]
    public string TargetBranch { get; set; } = "";

    [Option("remote", Required = false, HelpText = "Remote to fetch from", Default = "origin")]
    public string Remote { get; set; } = "origin";

    [Option("no-fetch", Required = false,Default = false, HelpText = "Skip fetching latest changes")]
    public bool NoFetch { get; set; }

    [Option("timeout", Required = false, HelpText = "Timeout in seconds", Default = 300)]
    public int TimeoutSeconds { get; set; } = 300;

    [Option("exclude-file", Required = false, HelpText = "File name to exclude from analysis. Can be specified multiple times.")]
    public IEnumerable<string> ExcludeFiles { get; set; } = new List<string>();


    [Option("ticket-prefix", Required = true, HelpText = "Jira ticket prefix (e.g., HSAMED).")]
    public string TicketPrefix { get; set; } = "";


    [Option("output-dir", Required = false, HelpText = "Output directory for HTML export (defaults to ./reports)",Default = "./reports")]
    public string? OutputDir { get; set; }
}

