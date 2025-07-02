# Service Configuration Improvement

## Original Code
```csharp
.ConfigureServices((_, services) =>
{
    // Register all services using extension method
    services.AddHttpClient();
    services.AddCherryPickSmartServices();
});
```

## Improved Version

### Option 1: Minimal Enhancement
```csharp
.ConfigureServices((hostContext, services) =>
{
    // Configure HttpClient with named clients for better control
    services.AddHttpClient("Default")
        .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));
    
    // Register all services using extension method
    services.AddCherryPickSmartServices();
    
    // Add configuration binding
    services.AddSingleton(hostContext.Configuration);
});
```

### Option 2: Production-Ready Enhancement
```csharp
.ConfigureServices((hostContext, services) =>
{
    // Bind configuration
    var configuration = hostContext.Configuration;
    services.Configure<ApplicationSettings>(configuration.GetSection("CherryPickSmart"));
    
    // Configure HttpClient with resilience
    services.AddHttpClient("JiraClient", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "CherryPickSmart/1.0");
    })
    .AddTransientHttpErrorPolicy(policy => 
        policy.WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))
    .AddTransientHttpErrorPolicy(policy => 
        policy.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));
    
    // Add logging
    services.AddLogging(builder =>
    {
        builder.AddConfiguration(configuration.GetSection("Logging"));
        builder.AddConsole();
    });
    
    // Register all services using extension method
    services.AddCherryPickSmartServices();
});
```

### Option 3: Full-Featured Enhancement (with all best practices)
```csharp
.ConfigureAppConfiguration((hostContext, config) =>
{
    // Load configuration from multiple sources
    config.AddJsonFile("appsettings.json", optional: true)
          .AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", optional: true)
          .AddEnvironmentVariables("CHERRYPICK_");
})
.ConfigureServices((hostContext, services) =>
{
    // Configuration
    services.AddOptions();
    services.Configure<ApplicationSettings>(hostContext.Configuration.GetSection("CherryPickSmart"));
    
    // HttpClient with resilience policies
    services.AddHttpClient<JiraClient>("JiraClient", (serviceProvider, client) =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<ApplicationSettings>>().Value;
        if (!string.IsNullOrEmpty(settings.Jira?.Url))
        {
            client.BaseAddress = new Uri(settings.Jira.Url);
        }
        client.Timeout = TimeSpan.FromSeconds(settings.Jira?.TimeoutSeconds ?? 30);
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
    
    // Logging
    services.AddLogging(logging =>
    {
        logging.AddConfiguration(hostContext.Configuration.GetSection("Logging"));
        logging.AddConsole();
        if (hostContext.HostingEnvironment.IsDevelopment())
        {
            logging.AddDebug();
        }
    });
    
    // Register all services
    services.AddCherryPickSmartServices();
    
    // Health checks
    services.AddHealthChecks()
        .AddCheck("git", () => HealthCheckResult.Healthy("Git is available"));
});

// Helper methods for resilience policies
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
```

## Key Improvements Explained

1. **Use hostContext instead of discarding it (_)**
   - Provides access to configuration and environment information

2. **Named HttpClient instances**
   - Better control over different HTTP clients for different services
   - Avoids socket exhaustion issues

3. **Resilience policies**
   - Retry policy for transient failures
   - Circuit breaker to prevent cascading failures

4. **Configuration binding**
   - Strongly-typed configuration using Options pattern
   - Multiple configuration sources support

5. **Structured logging**
   - Proper logging configuration
   - Environment-specific logging levels

6. **Health checks**
   - Monitor application and dependency health

## Required NuGet Packages

For the full-featured version, add:
```xml
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="8.0.0" />
```

## Recommendation

- For a simple console application: Use Option 1
- For production use with external API calls: Use Option 2
- For enterprise-grade applications: Use Option 3
