using System.Diagnostics;
using System.Text;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace CherryPickSmart.Core.Integration;

public class GitCommandExecutor
{
    private readonly ILogger<GitCommandExecutor> _logger;

    public GitCommandExecutor(ILogger<GitCommandExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(string command)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null) error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"Git command failed: {command}\nError: {error}");
        }

        return output.ToString();
    }

    public bool IsGitRepository(string repositoryPath)
    {
        try
        {
            using var repo = new Repository(repositoryPath);
            return true;
        }
        catch (RepositoryNotFoundException)
        {
            _logger.LogWarning($"No Git repository found at path: {repositoryPath}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error checking Git repository: {ex.Message}");
            return false;
        }
    }

    public IEnumerable<Commit> GetCommits(string repositoryPath, string branchName)
    {
        try
        {
            using var repo = new Repository(repositoryPath);
            var branch = repo.Branches[branchName];
            if (branch == null)
                throw new GitCommandException($"Branch '{branchName}' not found in repository.");

            return branch.Commits;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error retrieving commits: {ex.Message}");
            throw new GitCommandException($"Failed to retrieve commits for branch '{branchName}'.");
        }
    }
}

public class GitCommandException : Exception
{
    public GitCommandException(string message) : base(message) { }
}
