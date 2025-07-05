using System;
using System.Text.RegularExpressions;
using LibGit2Sharp;

namespace GitCherryHelper;

public class BranchValidator
{
    private readonly Repository _repo;
    private static readonly Regex BranchNameRegex = new(@"^[a-zA-Z0-9/_.-]+$", RegexOptions.Compiled);

    public BranchValidator(Repository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public void ValidateBranches(string sourceBranch, string targetBranch)
    {
        if (string.IsNullOrWhiteSpace(sourceBranch))
            throw new ArgumentException("Source branch name cannot be null or empty", nameof(sourceBranch));
        
        if (string.IsNullOrWhiteSpace(targetBranch))
            throw new ArgumentException("Target branch name cannot be null or empty", nameof(targetBranch));

        ValidateBranchName(sourceBranch, nameof(sourceBranch));
        ValidateBranchName(targetBranch, nameof(targetBranch));

        var source = GetBranch(sourceBranch);
        var target = GetBranch(targetBranch);

        if (source == null)
            throw new ArgumentException($"Source branch '{sourceBranch}' not found in repository", nameof(sourceBranch));
        
        if (target == null)
            throw new ArgumentException($"Target branch '{targetBranch}' not found in repository", nameof(targetBranch));

        // Additional validation - ensure branches are different
        if (string.Equals(sourceBranch, targetBranch, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and target branches cannot be the same");
    }

    public Branch GetBranch(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return null;

        ValidateBranchName(branchName, nameof(branchName));

        try
        {
            // Try local branch first
            var localBranch = _repo.Branches[branchName];
            if (localBranch != null)
                return localBranch;

            // Try remote branch
            var remoteBranch = _repo.Branches[$"origin/{branchName}"];
            if (remoteBranch != null)
                return remoteBranch;

            // Try other remotes
            foreach (var remote in _repo.Network.Remotes)
            {
                var remoteTrackingBranch = _repo.Branches[$"{remote.Name}/{branchName}"];
                if (remoteTrackingBranch != null)
                    return remoteTrackingBranch;
            }

            return null;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to resolve branch '{branchName}': {ex.Message}", ex);
        }
    }

    private static void ValidateBranchName(string branchName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            throw new ArgumentException("Branch name cannot be null or empty", parameterName);

        if (!BranchNameRegex.IsMatch(branchName))
            throw new ArgumentException($"Invalid branch name format: {branchName}. Branch names can only contain letters, numbers, underscores, hyphens, dots, and forward slashes.", parameterName);

        // Additional security checks
        if (branchName.Contains("..") || branchName.Contains("$") || branchName.Contains("`") || 
            branchName.Contains(";") || branchName.Contains("&") || branchName.Contains("|") ||
            branchName.Contains("*") || branchName.Contains("?") || branchName.Contains("[") ||
            branchName.Contains("]") || branchName.Contains("~") || branchName.Contains("^"))
            throw new ArgumentException($"Branch name contains invalid characters: {branchName}", parameterName);

        // Check for invalid patterns
        if (branchName.StartsWith("-") || branchName.EndsWith(".") || branchName.Contains("/.") ||
            branchName.Contains("..") || branchName.Contains("@{"))
            throw new ArgumentException($"Branch name contains invalid patterns: {branchName}", parameterName);

        // Check maximum length (Git limit is 255 characters)
        if (branchName.Length > 255)
            throw new ArgumentException($"Branch name too long (max 255 characters): {branchName}", parameterName);
    }
}
