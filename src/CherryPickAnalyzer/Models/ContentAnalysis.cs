namespace CherryPickAnalyzer.Models;

public class ContentAnalysis
{
    public List<FileChange> ChangedFiles { get; set; } = [];
    public DiffStats Stats { get; set; } = new();
    public List<TicketGroup> TicketGroups { get; set; } = [];
}

public class TicketGroup
{
    public string TicketNumber { get; set; } = "";
    public CherryPickAnalyzer.Helpers.CherryPickHelper.JiraTicketInfo? JiraInfo { get; set; }
    public List<MergeRequestInfo> MergeRequests { get; set; } = [];
    public List<CommitInfo> StandaloneCommits { get; set; } = [];
}

public class MergeRequestInfo
{
    public CommitInfo MergeCommit { get; set; } = new();
    public List<CommitInfo> MrCommits { get; set; } = [];
    public List<string> CherryPickShas { get; set; } = [];
}

// Note: Using CherryPickHelper.JiraTicketInfo instead of duplicating the class