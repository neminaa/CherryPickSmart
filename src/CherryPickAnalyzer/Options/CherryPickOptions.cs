using CommandLine;

namespace GitCherryHelper.Options;

[Verb("cherry-pick", HelpText = "Generate cherry-pick commands")]
public class CherryPickOptions
{
    [Option('r', "repo", Required = false, HelpText = "Path to git repository", Default = ".")]
    public string RepoPath { get; set; } = ".";

    [Option('s', "source", Required = true, HelpText = "Source branch")]
    public string SourceBranch { get; set; } = "";

    [Option('t', "target", Required = true, HelpText = "Target branch")]
    public string TargetBranch { get; set; } = "";

    [Option("execute", Required = false, HelpText = "Execute cherry-pick commands")]
    public bool Execute { get; set; }

    [Option("interactive", Required = false, HelpText = "Interactive cherry-pick selection")]
    public bool Interactive { get; set; }
}