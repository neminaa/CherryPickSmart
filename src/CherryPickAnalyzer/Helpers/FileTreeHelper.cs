using Spectre.Console;
using LibGit2Sharp;
using CherryPickAnalyzer.Models;

namespace CherryPickAnalyzer.Helpers;

public static class FileTreeHelper
{
    public static void AddFileToTree(Spectre.Console.Tree tree, string filePath, TreeNode fileNode)
    {
        var pathParts = filePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        object currentLevel = tree;
        for (var i = 0; i < pathParts.Length - 1; i++)
        {
            var part = Markup.Escape(pathParts[i]);
            var nodes = currentLevel is Spectre.Console.Tree t ? t.Nodes : ((TreeNode)currentLevel).Nodes;
            var existing = nodes.FirstOrDefault(n => n.ToString()!.Contains(part));
            TreeNode currentNode;
            if (existing == null)
            {
                var dirNode = new TreeNode(new Markup($"[blue]📁 {part}[/]"));
                nodes.Add(dirNode);
                currentNode = dirNode;
            }
            else
            {
                currentNode = existing;
            }
            currentLevel = currentNode;
        }
        var fileParentNodes = currentLevel is Spectre.Console.Tree t2 ? t2.Nodes : ((TreeNode)currentLevel).Nodes;
        fileParentNodes.Add(fileNode);
    }

    public static List<FileChange> GetFilesChangedByCommit(Repository repo, Commit commit)
    {
        var result = new List<FileChange>();
        var parent = commit.Parents.FirstOrDefault();
        if (parent == null) return result;
        var patch = repo.Diff.Compare<Patch>(parent.Tree, commit.Tree);
        foreach (var change in patch)
        {
            result.Add(new FileChange
            {
                NewPath = change.Path,
                Status = change.Status.ToString(),
                LinesAdded = change.LinesAdded,
                LinesDeleted = change.LinesDeleted
            });
        }
        return result;
    }
} 