namespace GitCherryHelper.Models;

public class RepositoryStatus
{
    public bool HasUncommittedChanges { get; set; }
    public List<string> UntrackedFiles { get; set; } = [];
    public List<string> ModifiedFiles { get; set; } = [];
    public List<string> StagedFiles { get; set; } = [];
}