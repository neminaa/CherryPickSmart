namespace CherryPickAnalyzer.Models;

public class FileChange
{
    public string NewPath { get; set; } = "";
    public string Status { get; set; } = "";
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
    public List<CommitInfo> Commits { get; set; } = new();
}