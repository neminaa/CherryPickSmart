using System;
using LibGit2Sharp;

namespace GitCherryHelper.Core;

public class BranchValidator
{
    private readonly Repository _repo;

    public BranchValidator(Repository repo)
    {
        _repo = repo;
    }

    public void ValidateBranches(string sourceBranch, string targetBranch)
    {
        var source = GetBranch(sourceBranch);
        var target = GetBranch(targetBranch);

        if (source == null)
            throw new ArgumentException($"Source branch '{sourceBranch}' not found");
        if (target == null)
            throw new ArgumentException($"Target branch '{targetBranch}' not found");
    }

    public Branch GetBranch(string branchName)
    {
        return _repo.Branches[branchName] ??
               _repo.Branches[$"origin/{branchName}"];
    }
}
