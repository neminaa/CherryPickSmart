using CommandLine;
using GitCherryHelper.Core;
using GitCherryHelper.Options;
using Spectre.Console;



return await Parser.Default.ParseArguments<AnalyzeOptions, CherryPickOptions>(args)
    .MapResult(
        (AnalyzeOptions opts) => RunAnalyzeAsync(opts),
        (CherryPickOptions opts) => RunCherryPickAsync(opts),
        errs => Task.FromResult(1));



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

static async Task<int> RunCherryPickAsync(CherryPickOptions options)
{
    try
    {
        using var cli = new GitDeploymentCli(options.RepoPath);
        return await cli.CherryPickAsync(options);
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteException(ex);
        return 1;
    }
}