using CherryPickSmart.Models;
using Scriban;

namespace CherryPickSmart.Services
{
    public static class HtmlReportGenerator
    {
        /// <summary>
        /// Generate comprehensive HTML report from AnalysisResult
        /// </summary>
        public static string GenerateHtmlReport(AnalysisResult result)
        {
            try
            {
                var templatePath = Path.Combine("Templates", "Report.html.scriban");

                if (!File.Exists(templatePath))
                {
                    throw new FileNotFoundException($"Template file not found: {templatePath}");
                }

                var templateText = File.ReadAllText(templatePath);
                var template = Template.Parse(templateText);

                if (template.HasErrors)
                {
                    var errors = string.Join("\n", template.Messages.Select(m => $"- {m}"));
                    throw new InvalidOperationException($"Template parsing errors:\n{errors}");
                }

                // Convert AnalysisResult to template-friendly object
                var templateData = CreateTemplateData(result);

                var html = template.Render(templateData);
                return html;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to generate HTML report: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Convert AnalysisResult to Scriban-friendly object with proper naming
        /// </summary>
        private static object CreateTemplateData(AnalysisResult result)
        {
            return new
            {
                // Basic Info
                from_branch = result.FromBranch,
                to_branch = result.ToBranch,
                analysis_timestamp = result.AnalysisTimestamp,
                analysis_id = result.AnalysisId,

                // Statistics
                statistics = new
                {
                    total_commits_analyzed = result.Statistics.TotalCommitsAnalyzed,
                    commits_with_tickets = result.Statistics.CommitsWithTickets,
                    orphan_commits = result.Statistics.OrphanCommits,
                    merge_commits = result.Statistics.MergeCommits,
                    total_tickets = result.Statistics.TotalTickets,
                    predicted_conflicts = result.Statistics.PredictedConflicts,
                    ticket_coverage = result.Statistics.TicketCoverage * 100, // Convert to percentage
                    analysis_duration = new
                    {
                        total_seconds = result.Statistics.AnalysisDuration.TotalSeconds
                    }
                },

                // Ticket Analyses
                ticket_analyses = result.TicketAnalyses.Select(ticket => new
                {
                    ticket_key = ticket.TicketKey,
                    priority = ticket.Priority.ToString(),
                    total_files_modified = ticket.TotalFilesModified,
                    authors = ticket.Authors,

                    // Commits
                    all_commits = ticket.AllCommits.Select(FormatCommit).ToList(),
                    regular_commits = ticket.RegularCommits.Select(FormatCommit).ToList(),
                    merge_commits = ticket.MergeCommits.Select(FormatCommit).ToList(),

                    // Strategy
                    recommended_strategy = new
                    {
                        type = ticket.RecommendedStrategy.Type.ToString(),
                        description = ticket.RecommendedStrategy.Description
                    }
                }).ToList(),

                // Conflict Analysis
                conflict_analysis = new
                {
                    all_conflicts = result.ConflictAnalysis.AllConflicts.Select(conflict => new
                    {
                        file = conflict.File,
                        risk = conflict.Risk.ToString(),
                        type = conflict.Type.ToString(),
                        description = conflict.Description,
                        conflicting_commits = conflict.ConflictingCommits.Select(FormatCommit).ToList()
                    }).ToList(),
                    statistics = new
                    {
                        total_conflicts = result.ConflictAnalysis.Statistics.TotalConflicts,
                        high_risk_conflicts = result.ConflictAnalysis.Statistics.HighRiskConflicts,
                        files_affected = result.ConflictAnalysis.Statistics.FilesAffected
                    }
                },

                // Orphan Analysis
                orphan_analysis = new
                {
                    orphan_commits = result.OrphanAnalysis.OrphanCommits.Select(orphan => new
                    {
                        commit = FormatCommit(orphan.Commit),
                        reason = orphan.Reason,
                        severity = orphan.Severity.ToString(),
                        suggestions = orphan.Suggestions.Select(suggestion => new
                        {
                            ticket_key = suggestion.TicketKey,
                            confidence = suggestion.Confidence,
                            type = suggestion.Type.ToString()
                        }).ToList()
                    }).ToList(),
                    statistics = new
                    {
                        total_orphans = result.OrphanAnalysis.Statistics.TotalOrphans,
                        orphans_with_suggestions = result.OrphanAnalysis.Statistics.OrphansWithSuggestions,
                        high_priority_orphans = result.OrphanAnalysis.Statistics.HighPriorityOrphans
                    }
                },

                // Recommended Plan
                recommended_plan = new
                {
                    type = result.RecommendedPlan.Type.ToString(),
                    summary = result.RecommendedPlan.Summary,
                    risk_assessment = new
                    {
                        overall_risk_score = result.RecommendedPlan.RiskAssessment.OverallRiskScore,
                        risk_level = result.RecommendedPlan.RiskAssessment.RiskLevel,
                        top_risks = result.RecommendedPlan.RiskAssessment.TopRisks
                    }
                },

                // Recommendations
                recommendations = result.Recommendations.Select(rec => new
                {
                    type = rec.Type.ToString(),
                    title = rec.Title,
                    description = rec.Description,
                    priority = rec.Priority.ToString()
                }).ToList()
            };
        }

        /// <summary>
        /// Format commit for template consumption
        /// </summary>
        private static object FormatCommit(CpCommit commit)
        {
            return new
            {
                sha = commit.Sha,
                short_sha = commit.ShortSha,
                message = TruncateMessage(commit.Message),
                author = commit.Author,
                timestamp = commit.Timestamp,
                modified_files = commit.ModifiedFiles,
                is_merge_commit = commit.IsMergeCommit
            };
        }

        /// <summary>
        /// Truncate commit message for display
        /// </summary>
        private static string TruncateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return "";

            // Get first line only
            var firstLine = message.Split('\n')[0];

            // Truncate if too long
            return firstLine.Length > 80 ? firstLine.Substring(0, 77) + "..." : firstLine;
        }

        /// <summary>
        /// Save HTML report to file with error handling
        /// </summary>
        public static async Task<string> SaveHtmlReportAsync(AnalysisResult result, string? outputDirectory = null)
        {
            try
            {
                var html = GenerateHtmlReport(result);

                var outputDir = outputDirectory ?? Directory.GetCurrentDirectory();
                Directory.CreateDirectory(outputDir);

                var fileName = result.Export.MarkdownReport?.FileName?.Replace(".md", ".html")
                             ?? $"cherrypick_report_{DateTime.Now:yyyyMMdd_HHmmss}.html";

                var filePath = Path.Combine(outputDir, fileName);

                await File.WriteAllTextAsync(filePath, html);

                return filePath;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save HTML report: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generate and save report in one step
        /// </summary>
        public static async Task<string> GenerateAndSaveReportAsync(AnalysisResult result, string? outputDirectory = null)
        {
            var filePath = await SaveHtmlReportAsync(result, outputDirectory);

            Console.WriteLine($"✅ HTML report generated: {filePath}");

            // Try to open in browser if on Windows
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    Console.WriteLine("📋 Report opened in default browser");
                }
                catch
                {
                    // Ignore if can't open browser
                }
            }

            return filePath;
        }
    }
}