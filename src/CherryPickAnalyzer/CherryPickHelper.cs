using System.Collections.Generic;
using System.Linq;
using GitCherryHelper.Models;
using Spectre.Console;

namespace GitCherryHelper;

public static class CherryPickHelper
{
    public static List<CommitInfo> SelectCommitsInteractively(List<CommitInfo> commits)
    {
        var prompt = new MultiSelectionPrompt<CommitInfo>()
            .Title("Select commits to cherry-pick:")
            .PageSize(10)
            .UseConverter(commit => $"{commit.ShortSha} {commit.Message} ({commit.Author})")
            .AddChoices(commits);

        return AnsiConsole.Prompt(prompt);
    }

    public static List<string> GenerateCherryPickCommands(string targetBranch, List<CommitInfo> commits)
    {
        var commands = new List<string> { $"git checkout {targetBranch}" };
        commands.AddRange(commits.Select(c => $"git cherry-pick {c.Sha}"));
        return commands;
    }

    public static void DisplayCherryPickCommands(List<string> commands)
    {
        var table = new Table()
            .AddColumn("Step")
            .AddColumn("Command")
            .BorderColor(Color.Green);

        for (var i = 0; i < commands.Count; i++)
        {
            table.AddRow((i + 1).ToString(), $"[dim]{commands[i]}[/]");
        }

        AnsiConsole.Write(new Panel(table)
            .Header("🍒 Cherry-pick Commands")
            .BorderColor(Color.Green));
    }
}
