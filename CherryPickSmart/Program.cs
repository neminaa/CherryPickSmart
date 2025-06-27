using CherryPickSmart.Commands;
using CherryPickSmart.Core.ConflictAnalysis;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.Integration;
using CherryPickSmart.Core.TicketAnalysis;
using CherryPickSmart.Services;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var host = CreateHostBuilder(args).Build();

return await Parser.Default.ParseArguments<
        AnalyzeCommand,
        PlanCommand
        //        ExecuteCommand,
        //        ConfigCommand
    >(args)
    .MapResult<AnalyzeCommand, PlanCommand, Task<int>>(
        async cmd => await RunCommandAsync(host, cmd),
        async cmd => await RunCommandAsync(host, cmd),
//            async (ExecuteCommand cmd) => await RunCommandAsync(host, cmd),
//            async (ConfigCommand cmd) => await RunCommandAsync(host, cmd),
        _ => Task.FromResult(1));

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureServices((_, services) =>
        {
            // Register all services
            services.AddSingleton<GitHistoryParser>();
            services.AddSingleton<MergeCommitAnalyzer>();
            services.AddSingleton<OrderOptimizer>();
            services.AddSingleton<ConflictPredictor>();
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
