namespace CherryPickAnalyzer.Models;

public class ContentAnalysis
{
    public List<FileChange> ChangedFiles { get; set; } = [];
    public DiffStats Stats { get; set; } = new();
}