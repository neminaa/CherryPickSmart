using LibGit2Sharp;

namespace CherryPickSmart.Models;

public record CpCommit
{
    public string Sha { get; init; } = "";
    public string ShortSha => Sha[..8];
    public List<string> ParentShas { get; init; } = [];
    public string Message { get; init; } = "";
    public string Author { get; init; } = "";
    public DateTime Timestamp { get; init; }
    public List<string> ModifiedFiles { get; init; } = [];
    public bool IsMergeCommit => ParentShas.Count > 1;

    // Derived properties
    public List<string> ExtractedTickets { get; set; } = [];
    public string? InferredTicket { get; set; }
    public double InferenceConfidence { get; set; }

    public bool IsOrpahan => ExtractedTickets.Count == 0;
    public Commit Commit { get; init; }

    public CpCommit(Commit commit, List<string>? modifiedFiles = null)
    {
        Sha = commit.Sha;
        ParentShas = commit.Parents.Select(p => p.Sha).ToList();
        Timestamp = commit.Author.When.DateTime;
        Author = commit.Author.Name;
        Message = commit.Message;
        ModifiedFiles = modifiedFiles ?? commit.Tree.Select(entry => entry.Path).ToList();
        Commit = commit;
    }
}

public record CpCommitGraph
{
    public Dictionary<string, CpCommit> Commits { get; init; } = new();
    public Dictionary<string, List<string>> ChildrenMap { get; init; } = new();
    public string FromBranch { get; init; } = "";
    public string ToBranch { get; init; } = "";
}
