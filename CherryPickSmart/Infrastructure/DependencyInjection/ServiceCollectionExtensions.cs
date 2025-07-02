using CherryPickSmart.Core.ConflictAnalysis;
using CherryPickSmart.Core.GitAnalysis;
using CherryPickSmart.Core.Integration;
using CherryPickSmart.Core.TicketAnalysis;
using CherryPickSmart.Services;
using CherryPickSmart.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CherryPickSmart.Infrastructure.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Register all application services
        /// </summary>
        public static IServiceCollection AddCherryPickSmartServices(this IServiceCollection services)
        {
            // Register Git Analysis services
            services.AddGitAnalysisServices();
            
            // Register Conflict Analysis services
            services.AddConflictAnalysisServices();
            
            // Register Ticket Analysis services
            services.AddTicketAnalysisServices();
            
            // Register Integration services
            services.AddIntegrationServices();
            
            // Register Application services
            services.AddApplicationServices();
            
            return services;
        }
        
        private static IServiceCollection AddGitAnalysisServices(this IServiceCollection services)
        {
            services.AddSingleton<GitHistoryParser>();
            services.AddSingleton<MergeCommitAnalyzer>();
            
            return services;
        }
        
        private static IServiceCollection AddConflictAnalysisServices(this IServiceCollection services)
        {
            services.AddSingleton<OrderOptimizer>();
            services.AddSingleton<ConflictPredictor>();
            
            return services;
        }
        
        private static IServiceCollection AddTicketAnalysisServices(this IServiceCollection services)
        {
            services.AddSingleton<TicketExtractor>();
            services.AddSingleton<OrphanCommitDetector>();
            services.AddSingleton<TicketInferenceEngine>();
            
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
            services.AddSingleton<ConfigurationService>();
            
            // Report generation might maintain state, consider scoped
            services.AddScoped<IReportGenerator, ReportGenerator>();
            
            // Add new services
            services.AddSingleton<InteractivePromptService>();
            
            return services;
        }
    }
}
