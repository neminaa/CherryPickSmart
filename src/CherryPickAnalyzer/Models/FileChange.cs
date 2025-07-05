namespace GitCherryHelper.Models;

public class FileChange
{
    public string NewPath { get; set; } = "";
    public string Status { get; set; } = "";
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
    public string CommitSha { get; set; } = "";
    public string CommitMessage { get; set; } = "";
    public string Author { get; set; } = "";
}