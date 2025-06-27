using LibGit2Sharp;

namespace CherryPickSmart.Core.GitAnalysis;

public interface ICommitParser
{
    /// <summary>
    /// Parses commits from a repository and returns a list of commits.
    /// </summary>
    IEnumerable<Commit> ParseCommits(string repositoryPath, string fromBranch, string toBranch);
}