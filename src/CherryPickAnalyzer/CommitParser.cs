using System;
using System.Collections.Generic;
using System.Linq;
using GitCherryHelper.Models;

namespace GitCherryHelper;

public static class CommitParser
{
    public static List<CommitInfo> ParseCommitOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new List<CommitInfo>();

        var commits = new List<CommitInfo>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 5)
            {
                // Skip malformed lines - log warning in production
                continue;
            }

            // Validate and parse date
            if (!DateTimeOffset.TryParse(parts[4], out var date))
            {
                // Skip commits with invalid dates - log warning in production
                continue;
            }

            // Validate SHA format (basic check)
            var sha = parts[0].Trim();
            var shortSha = parts[1].Trim();
            
            if (string.IsNullOrWhiteSpace(sha) || string.IsNullOrWhiteSpace(shortSha))
            {
                // Skip commits with invalid SHA values
                continue;
            }

            // Basic SHA validation - should be hexadecimal
            if (!IsValidSha(sha) || !IsValidSha(shortSha))
            {
                // Skip commits with invalid SHA format
                continue;
            }

            commits.Add(new CommitInfo
            {
                Sha = sha,
                ShortSha = shortSha,
                Message = parts[2].Trim(),
                Author = parts[3].Trim(),
                Date = date
            });
        }

        return commits;
    }

    private static bool IsValidSha(string sha)
    {
        if (string.IsNullOrWhiteSpace(sha))
            return false;

        // SHA should be between 5-40 characters and only contain hex characters
        if (sha.Length < 5 || sha.Length > 40)
            return false;

        return sha.All(c => char.IsAsciiHexDigit(c));
    }
}
