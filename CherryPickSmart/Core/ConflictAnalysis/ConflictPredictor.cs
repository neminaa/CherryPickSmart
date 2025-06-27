using CherryPickSmart.Models;

namespace CherryPickSmart.Core.ConflictAnalysis;

public class ConflictPredictor
{
    public record ConflictPrediction
    {
        public string File { get; init; } = "";
        public List<Commit> ConflictingCommits { get; init; } = new();
        public ConflictRisk Risk { get; init; }
        public string Description { get; init; } = "";
    }

    public enum ConflictRisk { Low, Medium, High, Certain }

    public List<ConflictPrediction> PredictConflicts(
        List<Commit> commitsToCherry,
        HashSet<string> targetBranchCommits)
    {
        var predictions = new List<ConflictPrediction>();

        var fileCommitMap = new Dictionary<string, List<Commit>>();
        foreach (var commit in commitsToCherry)
        {
            foreach (var file in commit.ModifiedFiles)
            {
                if (!fileCommitMap.ContainsKey(file))
                    fileCommitMap[file] = new();
                fileCommitMap[file].Add(commit);
            }
        }

        foreach (var (file, commits) in fileCommitMap.Where(f => f.Value.Count > 1))
        {
            var risk = CalculateConflictRisk(file, commits, targetBranchCommits);

            if (risk > ConflictRisk.Low)
            {
                predictions.Add(new ConflictPrediction
                {
                    File = file,
                    ConflictingCommits = commits,
                    Risk = risk,
                    Description = GenerateRiskDescription(file, commits, risk)
                });
            }
        }

        return predictions.OrderByDescending(p => p.Risk).ToList();
    }

    private ConflictRisk CalculateConflictRisk(
        string file, 
        List<Commit> commits, 
        HashSet<string> targetBranchCommits)
    {
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);
        var timeFactor = timeSpan.TotalDays > 7 ? 2 : 1;

        var authorCount = commits.Select(c => c.Author).Distinct().Count();
        var authorFactor = authorCount > 1 ? 2 : 1;

        var modifiedInTarget = CheckFileModifiedInTarget(file, targetBranchCommits);
        var targetFactor = modifiedInTarget ? 3 : 1;

        var totalRisk = timeFactor * authorFactor * targetFactor;

        return totalRisk switch
        {
            >= 6 => ConflictRisk.High,
            >= 4 => ConflictRisk.Medium,
            _ => ConflictRisk.Low
        };
    }

    private bool CheckFileModifiedInTarget(string file, HashSet<string> targetBranchCommits)
    {
        // Simulate checking if the file was modified in the target branch
        return targetBranchCommits.Contains(file);
    }

    private string GenerateRiskDescription(string file, List<Commit> commits, ConflictRisk risk)
    {
        var authors = string.Join(", ", commits.Select(c => c.Author).Distinct());
        var timeSpan = commits.Max(c => c.Timestamp) - commits.Min(c => c.Timestamp);

        return $"File '{file}' has {commits.Count} conflicting commits with risk level '{risk}'. " +
               $"Authors involved: {authors}. Time span: {timeSpan.TotalDays} days.";
    }
}
