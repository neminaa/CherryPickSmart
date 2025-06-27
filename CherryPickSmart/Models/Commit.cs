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
