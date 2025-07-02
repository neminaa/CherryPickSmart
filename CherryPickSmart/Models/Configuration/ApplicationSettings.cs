using System.Collections.Generic;

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
