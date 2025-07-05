namespace GitCherryHelper.Models;

public class FileChange
{
    public string NewPath { get; set; } = "";
    public string Status { get; set; } = "";
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
}