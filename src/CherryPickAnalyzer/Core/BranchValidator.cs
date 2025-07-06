using LibGit2Sharp;

namespace CherryPickAnalyzer.Core;

public class BranchValidator(Repository repo)
{
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
        return repo.Branches[branchName] ??
               repo.Branches[$"origin/{branchName}"];
    }
}
