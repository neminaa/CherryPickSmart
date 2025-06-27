using CherryPickSmart.Models;

namespace CherryPickSmart.Services.Interfaces
{
    public interface IReportGenerator
    {
        /// <summary>
        /// Generate HTML report from AnalysisResult
        /// </summary>
        string GenerateHtmlReport(AnalysisResult result);
        
        /// <summary>
        /// Save HTML report to file
        /// </summary>
        Task<string> SaveHtmlReportAsync(AnalysisResult result, string? outputDirectory = null);
        
        /// <summary>
        /// Generate and save report in one step
        /// </summary>
        Task<string> GenerateAndSaveReportAsync(AnalysisResult result, string? outputDirectory = null);
    }
}
