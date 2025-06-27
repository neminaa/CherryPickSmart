using CherryPickSmart.Commands;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.Integration;
using CherryPickSmart.Core.TicketAnalysis;
using CherryPickSmart.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var host = CreateHostBuilder(args).Build();

return Parser.Default.ParseArguments<
    AnalyzeCommand,
    PlanCommand
    //        ExecuteCommand,
    //        ConfigCommand
    >(args)
    .MapResult(
        (AnalyzeCommand cmd) => RunCommandAsync(host, cmd).GetAwaiter().GetResult(),
        (PlanCommand cmd) => RunCommandAsync(host, cmd).GetAwaiter().GetResult(),
//            async (ExecuteCommand cmd) => await RunCommandAsync(host, cmd),
//            async (ConfigCommand cmd) => await RunCommandAsync(host, cmd),
        errs => 1);

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            // Register all services
            services.AddSingleton<GitHistoryParser>();
            services.AddSingleton<IMergeCommitAnalyzer,MergeCommitAnalyzer>();
            services.AddSingleton<TicketExtractor>();
            services.AddSingleton<OrphanCommitDetector>();
            services.AddSingleton<TicketInferenceEngine>();
            services.AddSingleton<JiraClient>();
            services.AddSingleton<ConfigurationService>();
            services.AddSingleton<GitCommandExecutor>();
        });

static async Task<int> RunCommandAsync<TCommand>(IHost host, TCommand command)
where TCommand : ICommand
{
    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;

    try
    {
        return await command.ExecuteAsync(services);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error executing command {typeof(TCommand).Name}: {ex.Message}");
        return 1;
    }
}
