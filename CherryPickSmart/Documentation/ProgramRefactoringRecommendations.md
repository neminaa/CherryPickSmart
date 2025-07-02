# Program.cs Refactoring Recommendations

## Current Code Analysis

The current `Program.cs` uses a minimal host builder pattern with basic service registration. Here are the identified areas for improvement:

## 1. Enhanced Service Configuration

### Current Code:
```csharp
.ConfigureServices((_, services) =>
{
    // Register all services using extension method
    services.AddHttpClient();
    services.AddCherryPickSmartServices();
});
```

### Improved Version:
```csharp
.ConfigureServices((hostContext, services) =>
{
    // Add configuration
    services.AddSingleton<IConfiguration>(hostContext.Configuration);
    
    // Configure HttpClient with better defaults
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
        logging.AddConsole();
        logging.AddDebug();
        logging.SetMinimumLevel(LogLevel.Information);
    });
    
    // Configure application settings
    services.Configure<ApplicationSettings>(
        hostContext.Configuration.GetSection("CherryPickSmart"));
    
    // Register all services using extension method
    services.AddCherryPickSmartServices();
    
    // Add health checks
    services.AddHealthChecks()
        .AddCheck("git", new GitHealthCheck())
        .AddCheck("jira", new JiraHealthCheck());
});
```

## 2. Full Program.cs Refactoring

Here's a complete refactored version with all improvements:

```csharp
using CherryPickSmart.Commands;
using CherryPickSmart.Infrastructure.DependencyInjection;
using CherryPickSmart.Infrastructure.HealthChecks;
using CherryPickSmart.Models.Configuration;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

// Configure and build the host
var host = CreateHostBuilder(args).Build();

// Parse and execute commands
return await Parser.Default.ParseArguments<
        AnalyzeCommand,
        PlanCommand,
        ExecuteCommand,
        ConfigCommand,
        HealthCheckCommand
    >(args)
    .MapResult(
        async (ICommand cmd) => await RunCommandAsync(host, cmd),
        errors => Task.FromResult(HandleParseErrors(errors)));

static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((hostContext, config) =>
        {
            config
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", 
                    optional: true, reloadOnChange: true)
                .AddJsonFile(GetUserConfigPath(), optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "CHERRYPICK_")
                .AddCommandLine(args);
        })
        .ConfigureServices((hostContext, services) =>
        {
            // Add configuration
            services.AddSingleton<IConfiguration>(hostContext.Configuration);
            
            // Configure application settings
            services.Configure<ApplicationSettings>(
                hostContext.Configuration.GetSection("CherryPickSmart"));
            
            // Configure HttpClient with resilience policies
            services.AddHttpClient("JiraClient", (serviceProvider, client) =>
            {
                var config = serviceProvider.GetRequiredService<IConfiguration>();
                var jiraUrl = config["CherryPickSmart:JiraUrl"];
                if (!string.IsNullOrEmpty(jiraUrl))
                {
                    client.BaseAddress = new Uri(jiraUrl);
                }
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "CherryPickSmart/1.0");
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddPolicyHandler(GetCircuitBreakerPolicy());
            
            // Add logging with configuration
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConfiguration(hostContext.Configuration.GetSection("Logging"));
                logging.AddConsole();
                logging.AddDebug();
                
                if (hostContext.HostingEnvironment.IsDevelopment())
                {
                    logging.AddFilter("Microsoft", LogLevel.Debug);
                    logging.AddFilter("System", LogLevel.Debug);
                }
            });
            
            // Register all application services
            services.AddCherryPickSmartServices();
            
            // Add health checks
            services.AddHealthChecks()
                .AddCheck<GitHealthCheck>("git")
                .AddCheck<JiraHealthCheck>("jira", tags: new[] { "external" });
            
            // Add telemetry/metrics (optional)
            services.AddApplicationInsightsTelemetry();
        })
        .UseConsoleLifetime();

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

static int HandleParseErrors(IEnumerable<Error> errors)
{
    var isVersionRequest = errors.Any(e => e is VersionRequestedError);
    var isHelpRequest = errors.Any(e => e is HelpRequestedError);
    
    if (isVersionRequest || isHelpRequest)
    {
        return 0; // Success for help/version requests
    }
    
    Console.Error.WriteLine("Failed to parse command line arguments.");
    return 1;
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
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                var logger = context.Values.ContainsKey("logger") 
                    ? context.Values["logger"] as ILogger 
                    : null;
                logger?.LogWarning("Delaying for {delay}ms, then making retry {retry}.", 
                    timespan.TotalMilliseconds, retryCount);
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
```

## 3. Service Registration Improvements

Update `ServiceCollectionExtensions.cs` to use appropriate lifetimes:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCherryPickSmartServices(this IServiceCollection services)
    {
        // Register with appropriate lifetimes
        services.AddGitAnalysisServices();
        services.AddConflictAnalysisServices();
        services.AddTicketAnalysisServices();
        services.AddIntegrationServices();
        services.AddApplicationServices();
        
        return services;
    }
    
    private static IServiceCollection AddGitAnalysisServices(this IServiceCollection services)
    {
        // Stateless services can be singleton
        services.AddSingleton<GitHistoryParser>();
        services.AddSingleton<MergeCommitAnalyzer>();
        
        return services;
    }
    
    private static IServiceCollection AddIntegrationServices(this IServiceCollection services)
    {
        // HttpClient-based services should be scoped or transient
        services.AddScoped<JiraClient>();
        services.AddScoped<GitCommandExecutor>();
        
        return services;
    }
    
    private static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Configuration can be singleton
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        
        // Report generation might maintain state, consider scoped
        services.AddScoped<IReportGenerator, ReportGenerator>();
        
        // Add new services
        services.AddSingleton<IInteractivePromptService, InteractivePromptService>();
        
        return services;
    }
}
```

## 4. Additional Improvements

### 4.1 Create Configuration Models

```csharp
namespace CherryPickSmart.Models.Configuration
{
    public class ApplicationSettings
    {
        public JiraSettings Jira { get; set; } = new();
        public GitSettings Git { get; set; } = new();
        public List<string> TicketPrefixes { get; set; } = new() { "HSAMED" };
    }

    public class JiraSettings
    {
        public string? Url { get; set; }
        public string? Username { get; set; }
        public string? ApiToken { get; set; }
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class GitSettings
    {
        public string DefaultFromBranch { get; set; } = "deploy/dev";
        public string DefaultToBranch { get; set; } = "deploy/uat";
        public int MaxCommitsToAnalyze { get; set; } = 1000;
    }
}
```

### 4.2 Add Health Checks

```csharp
namespace CherryPickSmart.Infrastructure.HealthChecks
{
    public class GitHealthCheck : IHealthCheck
    {
        private readonly GitCommandExecutor _gitExecutor;

        public GitHealthCheck(GitCommandExecutor gitExecutor)
        {
            _gitExecutor = gitExecutor;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _gitExecutor.ExecuteAsync("--version", cancellationToken);
                return HealthCheckResult.Healthy($"Git is available: {result}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Git is not available", ex);
            }
        }
    }
}
```

### 4.3 Add appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "CherryPickSmart": {
    "Jira": {
      "Url": "",
      "Username": "",
      "ApiToken": "",
      "TimeoutSeconds": 30
    },
    "Git": {
      "DefaultFromBranch": "deploy/dev",
      "DefaultToBranch": "deploy/uat",
      "MaxCommitsToAnalyze": 1000
    },
    "TicketPrefixes": ["HSAMED", "JIRA"]
  }
}
```

## 5. Key Benefits

1. **Better Configuration Management**: Uses .NET's configuration system with multiple sources
2. **Resilience**: HTTP retry and circuit breaker policies for external calls
3. **Observability**: Structured logging and health checks
4. **Testability**: Proper DI setup makes unit testing easier
5. **Flexibility**: Environment-specific configurations
6. **Error Handling**: Comprehensive error handling with appropriate logging
7. **Performance**: Appropriate service lifetimes reduce memory usage

## 6. Required NuGet Packages

Add these to your `.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="8.0.0" />
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.22.0" />
