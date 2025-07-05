namespace GitCherryHelper.Models;

public class CommitInfo
{
    public string Sha { get; set; } = "";
    public string ShortSha { get; set; } = "";
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTimeOffset Date { get; set; }
}