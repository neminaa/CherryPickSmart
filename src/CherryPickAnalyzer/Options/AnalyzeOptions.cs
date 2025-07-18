﻿using CommandLine;

namespace GitCherryHelper.Options;

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

    [Option("no-fetch", Required = false,Default = true, HelpText = "Skip fetching latest changes")]
    public bool NoFetch { get; set; }

    [Option("format", Required = false, HelpText = "Output format (table, json, markdown)", Default = "table")]
    public string Format { get; set; } = "table";

    [Option('v', "verbose", Required = false, HelpText = "Verbose output")]
    public bool Verbose { get; set; }

    [Option("timeout", Required = false, HelpText = "Timeout in seconds", Default = 300)]
    public int TimeoutSeconds { get; set; } = 300;
}