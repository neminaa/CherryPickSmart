using CherryPickAnalyzer.Models;
using Scriban;
using System.Text;

namespace CherryPickAnalyzer.Services;

public class HtmlExportService
{
    private const string HtmlTemplate = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Cherry-Pick Analysis - {{ source_branch }} → {{ target_branch }}</title>
    <style>
        body { font-family: 'Segoe UI', Roboto, Arial, sans-serif; background: #f5f6fa; color: #222; }
        .container { max-width: 1100px; margin: 0 auto; padding: 32px 12px; }
        .ticket-card { background: #fff; border-radius: 10px; box-shadow: 0 2px 8px #0001; margin-bottom: 28px; padding: 28px; }
        .ticket-header { display: flex; align-items: center; gap: 16px; font-size: 1.2em; margin-bottom: 8px; }
        .ticket-key { font-weight: bold; color: #2563eb; }
        .status-badge { padding: 4px 12px; border-radius: 16px; font-size: 0.9em; font-weight: 600; background: #e0e7ff; color: #3730a3; }
        .status-badge.uat-deployed { background: #e0ffe0; color: #15803d; }
        .mr-count { margin-left: auto; color: #888; font-size: 0.95em; }
        .ticket-summary { color: #555; margin-bottom: 16px; }
        .commit-list { margin-left: 12px; border-left: 2px solid #e5e7eb; padding-left: 16px; }
        .mr-block { margin-bottom: 18px; }
        .commit { background: #f9fafb; border-radius: 6px; margin-bottom: 10px; padding: 10px 14px; }
        .commit .row1 { display: flex; align-items: center; gap: 10px; font-size: 1em; }
        .commit .icon { font-size: 1.1em; }
        .commit .sha { font-family: monospace; background: #f3f4f6; border-radius: 6px; padding: 2px 8px; font-size: 0.95em; color: #555; }
        .commit .message { margin-left: 8px; color: #222; font-weight: 500; }
        .commit .row2 { margin-top: 2px; font-size: 0.93em; color: #888; display: flex; gap: 12px; }
        .commit .author { font-weight: 400; }
        .commit .date { font-style: italic; }
        .commit.child { margin-left: 32px; background: #f3f4f6; }
        .dependencies { margin-top: 16px; padding: 12px; background: #f8fafc; border-radius: 6px; border-left: 3px solid #3b82f6; }
        .dependencies h4 { margin: 0 0 8px 0; font-size: 0.9em; color: #374151; }
        .dependency-list { display: flex; flex-wrap: wrap; gap: 8px; }
        .dependency-item { padding: 4px 8px; background: #e0e7ff; color: #3730a3; border-radius: 12px; font-size: 0.85em; font-weight: 500; cursor: pointer; transition: all 0.2s; }
        .dependency-item:hover { background: #c7d2fe; transform: translateY(-1px); }
        .dependency-item.dependent { background: #fef3c7; color: #92400e; }
        .dependency-item.dependent:hover { background: #fde68a; }
        .no-dependencies { color: #6b7280; font-style: italic; font-size: 0.9em; }
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🎫 Cherry-Pick Analysis</h1>
            <p><strong>{{ source_branch }}</strong> → <strong>{{ target_branch }}</strong></p>
            <p>Generated on {{ generated_date }}</p>
        </div>
        
        <div class=""summary"">
            <div class=""summary-card"">
                <h3>Total Tickets</h3>
                <div class=""number"">{{ ticket_groups.size }}</div>
            </div>
            <div class=""summary-card"">
                <h3>Total Commits</h3>
                <div class=""number"">{{ total_commits }}</div>
            </div>
            <div class=""summary-card"">
                <h3>Merge Requests</h3>
                <div class=""number"">{{ total_mrs }}</div>
            </div>
            <div class=""summary-card"">
                <h3>Standalone Commits</h3>
                <div class=""number"">{{ total_standalone }}</div>
            </div>
        </div>
        
        <div class=""ticket-groups"">
            {{ for ticket in ticket_groups }}
            <div class=""ticket-card"" id=""ticket-{{ ticket.ticket_number }}"">
                <div class=""ticket-header"">
                    <span class=""ticket-key"">{{ ticket.ticket_number }}</span>
                    {{ if ticket.jira_info }}
                        <span class=""status-badge"" style=""background: {{ ticket.status_color }}; color: #fff;"">{{ ticket.jira_info.status }}</span>
                    {{ end }}
                    <span class=""mr-count"">{{ ticket.merge_requests.size }} MRs, {{ ticket.standalone_commits.size }} standalone</span>
                </div>
                {{ if ticket.jira_info && ticket.jira_info.summary }}
                <div class=""ticket-summary"">{{ ticket.jira_info.summary }}</div>
                {{ end }}
                
                {{ if ticket.dependencies.size > 0 || ticket.dependents.size > 0 }}
                <div class=""dependencies"">
                    {{ if ticket.dependencies.size > 0 }}
                    <h4>🔗 Dependencies (required first):</h4>
                    <div class=""dependency-list"">
                        {{ for dep in ticket.dependencies }}
                        <div class=""dependency-item"" onclick=""scrollToTicket('{{ dep.ticket_number }}')"" style=""background: {{ dep.status_color }}20; color: {{ dep.status_color }};"">
                            {{ dep.ticket_number }} ({{ dep.status }})
                        </div>
                        {{ end }}
                    </div>
                    {{ end }}
                    
                    {{ if ticket.dependents.size > 0 }}
                    <h4>📋 Dependents (depend on this):</h4>
                    <div class=""dependency-list"">
                        {{ for dep in ticket.dependents }}
                        <div class=""dependency-item dependent"" onclick=""scrollToTicket('{{ dep.ticket_number }}')"" style=""background: {{ dep.status_color }}20; color: {{ dep.status_color }};"">
                            {{ dep.ticket_number }} ({{ dep.status }})
                        </div>
                        {{ end }}
                    </div>
                    {{ end }}
                </div>
                {{ end }}
                
                <div class=""commit-list"">
                    {{ for mr in ticket.merge_requests }}
                    <div class=""mr-block"">
                        <div class=""commit"">
                            <div class=""row1""><span class=""icon"">🔀</span><span class=""sha"">{{ mr.merge_commit.short_sha }}</span><span class=""message"">{{ mr.merge_commit.message }}</span></div>
                            <div class=""row2""><span class=""author"">{{ mr.merge_commit.author }}</span> | <span class=""date"">{{ mr.merge_commit.date }}</span></div>
                        </div>
                        {{ for commit in mr.mr_commits }}
                        <div class=""commit child"">
                            <div class=""row1""><span class=""icon"">📝</span><span class=""sha"">{{ commit.short_sha }}</span><span class=""message"">{{ commit.message }}</span></div>
                            <div class=""row2""><span class=""author"">{{ commit.author }}</span> | <span class=""date"">{{ commit.date }}</span></div>
                        </div>
                        {{ end }}
                    </div>
                    {{ end }}
                    {{ for commit in ticket.standalone_commits }}
                    <div class=""commit"">
                        <div class=""row1""><span class=""icon"">📝</span><span class=""sha"">{{ commit.short_sha }}</span><span class=""message"">{{ commit.message }}</span></div>
                        <div class=""row2""><span class=""author"">{{ commit.author }}</span> | <span class=""date"">{{ commit.date }}</span></div>
                    </div>
                    {{ end }}
                </div>
            </div>
            {{ end }}
        </div>
        
        <div class=""commands-panel"">
            <h3>🍒 Cherry-Pick Commands</h3>
            <div class=""command-list"" id=""commands"">Select tickets above to generate cherry-pick commands</div>
            <button onclick=""copyCommands()"" class=""copy-btn"">Copy Commands</button>
        </div>
    </div>
    
    <script>
        let selectedTickets = new Set();
        let allTickets = {{ ticket_groups_json }};
        
        function toggleTicket(header) {
            const content = header.nextElementSibling;
            content.classList.toggle('expanded');
        }
        
        function updateStatusFilters() {
            const filters = document.querySelectorAll('.status-filter');
            filters.forEach(filter => {
                const status = filter.dataset.status;
                const tickets = Array.from(document.querySelectorAll('.ticket-group')).filter(tg => {
                    const ticketData = allTickets.find(t => t.ticket_number === tg.dataset.ticket);
                    return ticketData && ticketData.jira_info && ticketData.jira_info.status === status;
                });
                const selectedCount = tickets.filter(tg => selectedTickets.has(tg.dataset.ticket)).length;
                const totalCount = tickets.length;
                filter.querySelector('.count').textContent = `${selectedCount}/${totalCount}`;
            });
        }
        
        function toggleTicketSelection(ticketElement) {
            const ticketNumber = ticketElement.dataset.ticket;
            if (selectedTickets.has(ticketNumber)) {
                selectedTickets.delete(ticketNumber);
                ticketElement.style.opacity = '0.6';
            } else {
                selectedTickets.add(ticketNumber);
                ticketElement.style.opacity = '1';
            }
            updateCommands();
            updateStatusFilters();
        }
        
        function selectAll() {
            document.querySelectorAll('.ticket-group').forEach(tg => {
                selectedTickets.add(tg.dataset.ticket);
                tg.style.opacity = '1';
            });
            updateCommands();
            updateStatusFilters();
        }
        
        function selectReady() {
            selectedTickets.clear();
            document.querySelectorAll('.ticket-group').forEach(tg => {
                const ticketData = allTickets.find(t => t.ticket_number === tg.dataset.ticket);
                if (ticketData && ticketData.jira_info) {
                    const status = ticketData.jira_info.status.toLowerCase();
                    if (status.includes('done') || status.includes('deployed') || status.includes('ready')) {
                        selectedTickets.add(tg.dataset.ticket);
                        tg.style.opacity = '1';
                    } else {
                        tg.style.opacity = '0.6';
                    }
                }
            });
            updateCommands();
            updateStatusFilters();
        }
        
        function clearSelection() {
            selectedTickets.clear();
            document.querySelectorAll('.ticket-group').forEach(tg => {
                tg.style.opacity = '0.6';
            });
            updateCommands();
            updateStatusFilters();
        }
        
        function updateCommands() {
            if (selectedTickets.size === 0) {
                document.getElementById('commands').textContent = 'Select tickets above to generate cherry-pick commands';
                return;
            }
            
            let commands = [`git checkout {{ target_branch }}`];
            let selectedTicketsData = allTickets.filter(t => selectedTickets.has(t.ticket_number));
            
            // Sort by date (oldest first)
            let allCommits = [];
            selectedTicketsData.forEach(ticket => {
                ticket.merge_requests.forEach(mr => {
                    allCommits.push({
                        sha: mr.merge_commit.sha,
                        date: new Date(mr.merge_commit.date),
                        type: 'merge',
                        ticket: ticket.ticket_number
                    });
                });
                ticket.standalone_commits.forEach(commit => {
                    allCommits.push({
                        sha: commit.sha,
                        date: new Date(commit.date),
                        type: 'standalone',
                        ticket: ticket.ticket_number
                    });
                });
            });
            
            allCommits.sort((a, b) => a.date - b.date);
            commands.push(...allCommits.map(c => `git cherry-pick ${c.sha}`));
            
            document.getElementById('commands').textContent = commands.join('\n');
        }
        
        function copyCommands() {
            const commands = document.getElementById('commands').textContent;
            if (commands && commands !== 'Select tickets above to generate cherry-pick commands') {
                navigator.clipboard.writeText(commands).then(() => {
                    const btn = event.target;
                    const originalText = btn.textContent;
                    btn.textContent = 'Copied!';
                    setTimeout(() => btn.textContent = originalText, 2000);
                });
            }
        }
        
        function scrollToTicket(ticketNumber) {
            const element = document.getElementById('ticket-' + ticketNumber);
            if (element) {
                element.scrollIntoView({ behavior: 'smooth', block: 'start' });
                // Highlight the ticket briefly
                element.style.boxShadow = '0 0 0 3px #3b82f6';
                setTimeout(() => {
                    element.style.boxShadow = '';
                }, 2000);
            }
        }
        
        // Initialize
        document.addEventListener('DOMContentLoaded', function() {
            updateStatusFilters();
            clearSelection();
        });
    </script>
</body>
</html>";

    public string GenerateHtml(DeploymentAnalysis analysis, string sourceBranch, string targetBranch)
    {
        var template = Template.Parse(HtmlTemplate);
        
        // Prepare data for template
        var ticketGroups = PrepareTicketGroups(analysis.ContentAnalysis.TicketGroups);
        var context = new
        {
            source_branch = sourceBranch,
            target_branch = targetBranch,
            generated_date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ticket_groups = ticketGroups,
            ticket_groups_json = System.Text.Json.JsonSerializer.Serialize(ticketGroups),
            available_statuses = GetAvailableStatuses(analysis.ContentAnalysis.TicketGroups),
            total_commits = analysis.ContentAnalysis.TicketGroups.Sum(tg => tg.MergeRequests.Count + tg.StandaloneCommits.Count),
            total_mrs = analysis.ContentAnalysis.TicketGroups.Sum(tg => tg.MergeRequests.Count),
            total_standalone = analysis.ContentAnalysis.TicketGroups.Sum(tg => tg.StandaloneCommits.Count)
        };
        
        return template.Render(context);
    }
    
    private List<object> PrepareTicketGroups(List<TicketGroup> ticketGroups)
    {
        var result = new List<object>();
        
        // First pass: create ticket lookup and analyze dependencies
        var ticketLookup = ticketGroups.ToDictionary(tg => tg.TicketNumber, tg => tg);
        var dependencies = AnalyzeTicketDependencies(ticketGroups);
        
        foreach (var ticketGroup in ticketGroups)
        {
            var ticketData = new
            {
                ticket_number = ticketGroup.TicketNumber,
                icon = ticketGroup.TicketNumber == "No Ticket" ? "❓" : "🎫",
                jira_info = ticketGroup.JiraInfo != null ? new
                {
                    status = ticketGroup.JiraInfo.Status,
                    summary = ticketGroup.JiraInfo.Summary,
                    key = ticketGroup.JiraInfo.Key
                } : null,
                status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280",
                merge_requests = ticketGroup.MergeRequests.Select(mr => new
                {
                    merge_commit = new
                    {
                        short_sha = mr.MergeCommit.ShortSha,
                        message = mr.MergeCommit.Message,
                        author = mr.MergeCommit.Author,
                        date = mr.MergeCommit.Date.ToString("yyyy-MM-dd")
                    },
                    mr_commits = mr.MrCommits.Select(c => new
                    {
                        short_sha = c.ShortSha,
                        message = c.Message,
                        author = c.Author,
                        date = c.Date.ToString("yyyy-MM-dd")
                    }).ToList()
                }).ToList(),
                standalone_commits = ticketGroup.StandaloneCommits.Select(c => new
                {
                    short_sha = c.ShortSha,
                    message = c.Message,
                    author = c.Author,
                    date = c.Date.ToString("yyyy-MM-dd")
                }).ToList(),
                dependencies = dependencies.GetValueOrDefault(ticketGroup.TicketNumber, new List<string>())
                    .Where(dep => ticketLookup.ContainsKey(dep))
                    .Select(dep => new
                    {
                        ticket_number = dep,
                        status = ticketLookup[dep].JiraInfo?.Status ?? "Unknown",
                        status_color = ticketLookup[dep].JiraInfo != null ? GetStatusColor(ticketLookup[dep].JiraInfo.Status) : "#6b7280"
                    }).ToList(),
                dependents = dependencies.Where(kvp => kvp.Value.Contains(ticketGroup.TicketNumber))
                    .Select(kvp => new
                    {
                        ticket_number = kvp.Key,
                        status = ticketLookup.GetValueOrDefault(kvp.Key)?.JiraInfo?.Status ?? "Unknown",
                        status_color = ticketLookup.GetValueOrDefault(kvp.Key)?.JiraInfo != null ? 
                            GetStatusColor(ticketLookup.GetValueOrDefault(kvp.Key)?.JiraInfo?.Status ?? "Unknown") : "#6b7280"
                    }).ToList()
            };
            
            result.Add(ticketData);
        }
        
        return result;
    }
    
    private Dictionary<string, List<string>> AnalyzeTicketDependencies(List<TicketGroup> ticketGroups)
    {
        var dependencies = new Dictionary<string, List<string>>();
        var ticketNumbers = ticketGroups.Select(tg => tg.TicketNumber).ToHashSet();
        
        foreach (var ticketGroup in ticketGroups)
        {
            if (ticketGroup.TicketNumber == "No Ticket") continue;
            
            var ticketDeps = new List<string>();
            
            // Check all commits in this ticket for dependency references
            var allCommits = ticketGroup.MergeRequests.SelectMany(mr => mr.MrCommits)
                .Concat(ticketGroup.StandaloneCommits)
                .ToList();
            
            foreach (var commit in allCommits)
            {
                // Look for ticket references in commit messages
                var referencedTickets = ExtractTicketReferences(commit.Message);
                foreach (var refTicket in referencedTickets)
                {
                    if (refTicket != ticketGroup.TicketNumber && ticketNumbers.Contains(refTicket))
                    {
                        if (!ticketDeps.Contains(refTicket))
                            ticketDeps.Add(refTicket);
                    }
                }
            }
            
            if (ticketDeps.Any())
                dependencies[ticketGroup.TicketNumber] = ticketDeps;
        }
        
        return dependencies;
    }

    private List<string> ExtractTicketReferences(string message)
    {
        var tickets = new List<string>();
        var words = message.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var word in words)
        {
            // Look for ticket patterns like HSAMED-1234, JIRA-567, etc.
            if (System.Text.RegularExpressions.Regex.IsMatch(word, @"^[A-Z]+-\d+$"))
            {
                tickets.Add(word);
            }
        }
        
        return tickets;
    }
    
    private List<object> GetAvailableStatuses(List<TicketGroup> ticketGroups)
    {
        var statuses = ticketGroups
            .Where(tg => tg.TicketNumber != "No Ticket" && tg.JiraInfo != null)
            .GroupBy(tg => tg.JiraInfo!.Status)
            .Select(g => new
            {
                name = g.Key,
                icon = GetStatusIcon(g.Key),
                count = g.Count()
            })
            .OrderBy(s => GetStatusPriority(s.name))
            .ToList<object>();
            
        // Add "No Ticket" if exists
        var noTicketGroup = ticketGroups.FirstOrDefault(tg => tg.TicketNumber == "No Ticket");
        if (noTicketGroup != null)
        {
            statuses.Add(new
            {
                name = "No Ticket",
                icon = "❓",
                count = 1
            });
        }
        
        return statuses;
    }
    
    private static string GetStatusIcon(string status)
    {
        return status.ToLower() switch
        {
            "to do" => "📋",
            "in progress" => "🔄",
            "pending prod deployment" => "⏳",
            "prod deployed" => "✅",
            "done" => "✅",
            "closed" => "🔒",
            _ => "🔄"
        };
    }
    
    private static string GetStatusColor(string status)
    {
        return status.ToLower() switch
        {
            "to do" => "#3b82f6",
            "in progress" => "#f59e0b",
            "pending prod deployment" => "#ef4444",
            "prod deployed" => "#10b981",
            "done" => "#10b981",
            "closed" => "#6b7280",
            _ => "#6b7280"
        };
    }
    
    private static int GetStatusPriority(string status)
    {
        return status.ToLower() switch
        {
            "to do" => 1,
            "in progress" => 2,
            "pending prod deployment" => 3,
            "prod deployed" => 4,
            "done" => 5,
            "closed" => 6,
            "unknown" => 99,
            _ => 50
        };
    }
} 