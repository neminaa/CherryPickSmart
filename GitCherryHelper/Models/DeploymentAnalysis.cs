namespace GitCherryHelper.Models;

public class DeploymentAnalysis
{
    public List<CommitInfo> OutstandingCommits { get; set; } = [];
    public CherryPickAnalysis CherryPickAnalysis { get; set; } = new();
    public bool HasContentDifferences { get; set; }
    public ContentAnalysis ContentAnalysis { get; set; } = new();
}