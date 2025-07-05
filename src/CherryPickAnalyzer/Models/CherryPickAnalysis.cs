namespace GitCherryHelper.Models;

public class CherryPickAnalysis
{
    public List<CommitInfo> NewCommits { get; set; } = [];
    public List<CommitInfo> AlreadyAppliedCommits { get; set; } = [];
}