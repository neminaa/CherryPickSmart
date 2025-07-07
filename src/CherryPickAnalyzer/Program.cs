using CherryPickAnalyzer.Core;
using CherryPickAnalyzer.Options;
using CommandLine;
using Spectre.Console;



return await Parser.Default.ParseArguments<AnalyzeOptions>(args)
    .MapResult(
        RunAnalyzeAsync,
        _ => Task.FromResult(1));



static async Task<int> RunAnalyzeAsync(AnalyzeOptions options)
{
    try
    {
        using var cli = new GitDeploymentCli(options.RepoPath);
        return await cli.AnalyzeAsync(options);
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteException(ex);
        return 1;
    }
}
