namespace GitCherryHelper.Models;

public class DiffStats
{
    public int FilesChanged { get; set; }
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
}