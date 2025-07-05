using System;
using System.Collections.Generic;
using GitCherryHelper.Models;

namespace GitCherryHelper;

public static class CommitParser
{
    public static List<CommitInfo> ParseCommitOutput(string output)
    {
        return new List<CommitInfo>(from line in output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            let parts = line.Split('|')
            where parts.Length >= 5
            select new CommitInfo
            {
                Sha = parts[0],
                ShortSha = parts[1],
                Message = parts[2],
                Author = parts[3],
                Date = DateTimeOffset.Parse(parts[4])
            });
    }
}
