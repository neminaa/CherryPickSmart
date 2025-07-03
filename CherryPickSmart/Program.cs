using CherryPickSmart.Commands;
using CherryPickSmart.Infrastructure.DependencyInjection;
using CherryPickSmart.Models.Configuration;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;


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
        .ConfigureAppConfiguration((hostContext, config) =>
        {
            config
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true)
                .AddJsonFile(GetUserConfigPath(), optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "CHERRYPICK_");
        })
        .ConfigureServices((hostContext, services) =>
        {
            // Add configuration
            services.AddSingleton(hostContext.Configuration);
            
            // Configure application settings
            services.Configure<ApplicationSettings>(
                hostContext.Configuration.GetSection("CherryPickSmart"));
            
            // Configure HttpClient with resilience
            services.AddHttpClient("JiraClient", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "CherryPickSmart/1.0");
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());
            
            // Add logging
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConfiguration(hostContext.Configuration.GetSection("Logging"));
                logging.AddConsole();
                if (hostContext.HostingEnvironment.IsDevelopment())
                {
                    logging.AddDebug();
                }
            });
            
            // Register all services using extension method
            services.AddCherryPickSmartServices();
        });

static async Task<int> RunCommandAsync<TCommand>(IHost host, TCommand command)
    where TCommand : ICommand
{
    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Executing command: {CommandType}", typeof(TCommand).Name);
        var result = await command.ExecuteAsync(services);
        logger.LogInformation("Command completed successfully");
        return result;
    }
    catch (OperationCanceledException)
    {
        logger.LogWarning("Command execution was cancelled");
        return 2;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error executing command {CommandType}", typeof(TCommand).Name);
        Console.Error.WriteLine($"Error: {ex.Message}");
        
        if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        return 1;
    }
}

static string GetUserConfigPath()
{
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cherry-pick-smart",
        "config.json");
}

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => !msg.IsSuccessStatusCode)
        .WaitAndRetryAsync(
            3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (_, timespan, retryCount, context) =>
            {
                Console.WriteLine($"Retry {retryCount} after {timespan} seconds");
            });
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            5,
            TimeSpan.FromSeconds(30),
            onBreak: (result, timespan) =>
            {
                Console.WriteLine($"Circuit breaker opened for {timespan}");
            },
            onReset: () =>
            {
                Console.WriteLine("Circuit breaker reset");
            });
}
