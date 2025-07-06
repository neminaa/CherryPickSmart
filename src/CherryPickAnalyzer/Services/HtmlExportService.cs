using CherryPickAnalyzer.Models;
using Scriban;
using System.Text;

namespace CherryPickAnalyzer.Services;

public class HtmlExportService
{
    private const string HtmlTemplate = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Cherry-Pick Analysis - {{ source_branch }} → {{ target_branch }}</title>
    <style>
        body { font-family: 'Segoe UI', Roboto, Arial, sans-serif; background: #f5f6fa; color: #222; margin: 0; }
        .container { max-width: 1100px; margin: 0 auto; padding: 32px 12px; }
        .header { margin-bottom: 32px; }
        .header h1 { font-size: 2.2em; font-weight: 700; display: flex; align-items: center; gap: 0.5em; margin: 0 0 16px 0; }
        .header .icon { font-size: 1.3em; }
        .header p { margin: 4px 0; color: #64748b; }
        .summary { display: flex; gap: 32px; margin-bottom: 36px; flex-wrap: wrap; }
        .summary-card { background: #fff; border-radius: 12px; box-shadow: 0 2px 8px rgba(0,0,0,0.06); padding: 24px 32px; flex: 1 1 180px; display: flex; flex-direction: column; align-items: center; min-width: 180px; }
        .summary-card .icon { font-size: 2em; margin-bottom: 8px; }
        .summary-card .number { font-size: 2.4em; font-weight: 700; color: #2563eb; margin-bottom: 4px; }
        .summary-card .label { font-size: 1.1em; color: #374151; font-weight: 500; }
        .search-panel { margin-bottom: 24px; display: flex; align-items: center; gap: 16px; }
        .search { padding: 8px 16px; border-radius: 8px; border: 1px solid #d1d5db; font-size: 1.1em; width: 320px; }
        
        /* Filter buttons */
        .filter-bar { margin-bottom: 18px; display: flex; gap: 10px; flex-wrap: wrap; }
        .filter-btn { 
            padding: 6px 16px; 
            border-radius: 20px; 
            border: 1px solid #e5e7eb; 
            background: #fff; 
            color: #374151; 
            font-size: 0.95em; 
            cursor: pointer; 
            transition: all 0.2s;
        }
        .filter-btn:hover { background: #f3f4f6; }
        .filter-btn.active { background: #2563eb; color: #fff; border-color: #2563eb; }
        
        /* Selection buttons */
        .selection-controls {
            display: flex;
            gap: 12px;
            margin-bottom: 24px;
        }
        .selection-btn {
            padding: 8px 16px;
            border-radius: 8px;
            border: 1px solid #d1d5db;
            background: #fff;
            color: #374151;
            cursor: pointer;
            font-size: 0.95em;
            transition: all 0.2s;
        }
        .selection-btn:hover { background: #f3f4f6; }
        
        .pagination { display: flex; gap: 6px; margin: 18px 0 0 0; list-style: none; justify-content: center; padding: 0; }
        .pagination li { display: inline-block; }
        .pagination li a { display: block; padding: 6px 12px; border-radius: 6px; background: #f3f4f6; color: #2563eb; text-decoration: none; font-weight: 500; transition: background 0.2s; }
        .pagination li.active a, .pagination li a:hover { background: #2563eb; color: #fff; }
        
        .ticket-groups { margin-top: 12px; }
        .ticket-card { 
            background: #fff; 
            border-radius: 10px; 
            box-shadow: 0 2px 8px rgba(0,0,0,0.06); 
            margin-bottom: 28px; 
            padding: 28px; 
            position: relative;
            transition: all 0.2s;
            display: flex;
            align-items: stretch;
        }
        .ticket-card.selected { box-shadow: 0 0 0 2px #2563eb; }
        .ticket-header { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; }
        .ticket-key { font-size: 1.3em; font-weight: 600; color: #2563eb; }
        .status-badge { padding: 2px 10px; border-radius: 8px; font-size: 0.95em; font-weight: 500; margin-left: 8px; }
        .mr-count { margin-left: auto; color: #64748b; font-size: 0.95em; }
        .ticket-summary { color: #374151; font-size: 1.05em; margin-bottom: 10px; }
        
        /* Checkbox styling */
        .ticket-checkbox-outer {
            display: flex;
            align-items: center;
            justify-content: center;
            min-width: 48px;
            padding-right: 8px;
        }
        .ticket-checkbox {
            width: 20px;
            height: 20px;
            cursor: pointer;
        }
        
        .commit-list { margin-top: 10px; }
        .mr-block { margin-bottom: 10px; }
        .commit { background: #f3f4f6; border-radius: 6px; padding: 10px 14px; margin-bottom: 6px; }
        .commit .row1 { display: flex; align-items: center; gap: 10px; font-size: 1.08em; }
        .commit .row2 { color: #64748b; font-size: 0.98em; margin-top: 2px; }
        .commit .icon { font-size: 1.1em; }
        .commit .sha { font-family: monospace; color: #2563eb; font-weight: 500; margin-right: 6px; }
        .commit .message { flex: 1; }
        .commit.child { margin-left: 32px; background: #f3f4f6; }
        
        .dependencies { margin-top: 16px; padding: 12px; background: #f8fafc; border-radius: 6px; border-left: 3px solid #3b82f6; }
        .dependencies h4 { margin: 0 0 8px 0; font-size: 0.9em; color: #374151; }
        .dependency-list { display: flex; flex-wrap: wrap; gap: 8px; }
        .dependency-item { padding: 4px 8px; background: #e0e7ff; color: #3730a3; border-radius: 12px; font-size: 0.85em; font-weight: 500; cursor: pointer; transition: all 0.2s; }
        .dependency-item:hover { background: #c7d2fe; transform: translateY(-1px); }
        .dependency-item.dependent { background: #fef3c7; color: #92400e; }
        .dependency-item.dependent:hover { background: #fde68a; }
        
        /* Commands panel */
        .commands-panel {
            background: #fff;
            border-radius: 12px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.06);
            padding: 24px;
            margin-top: 32px;
        }
        .commands-panel h3 {
            margin: 0 0 16px 0;
            font-size: 1.3em;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .command-list {
            background: #1e293b;
            color: #e2e8f0;
            padding: 16px;
            border-radius: 8px;
            font-family: 'Courier New', monospace;
            font-size: 0.95em;
            line-height: 1.6;
            white-space: pre-wrap;
            word-break: break-all;
            max-height: 300px;
            overflow-y: auto;
            margin-bottom: 16px;
        }
        .copy-btn {
            padding: 10px 24px;
            background: #2563eb;
            color: #fff;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            font-size: 1em;
            font-weight: 500;
            transition: background 0.2s;
        }
        .copy-btn:hover { background: #1d4ed8; }
        .copy-btn:active { transform: translateY(1px); }
    </style>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/list.js/2.3.1/list.min.js"></script>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1><span class="icon">🍒</span> Cherry-Pick Analysis</h1>
            <p><strong>{{ source_branch }}</strong> → <strong>{{ target_branch }}</strong></p>
            <p>Generated on {{ generated_date }}</p>
        </div>
        
        <div class="summary">
            <div class="summary-card">
                <span class="icon">🗂️</span>
                <div class="number">{{ ticket_groups.size }}</div>
                <div class="label">Total Tickets</div>
            </div>
            <div class="summary-card">
                <span class="icon">📝</span>
                <div class="number">{{ total_commits }}</div>
                <div class="label">Total Commits</div>
            </div>
            <div class="summary-card">
                <span class="icon">🔀</span>
                <div class="number">{{ total_mrs }}</div>
                <div class="label">Merge Requests</div>
            </div>
            <div class="summary-card">
                <span class="icon">📋</span>
                <div class="number">{{ total_standalone }}</div>
                <div class="label">Standalone Commits</div>
            </div>
        </div>
        
        <div class="search-panel">
            <input class="search" placeholder="Search tickets, status, summary..." />
        </div>
        
        <div class="selection-controls">
            <button class="selection-btn" onclick="selectAll()">Select All</button>
            <button class="selection-btn" onclick="selectReady()">Select Ready</button>
            <button class="selection-btn" onclick="clearSelection()">Clear Selection</button>
        </div>
        
        <div class="filter-bar" id="filter-bar">
            <button class="filter-btn active" data-status="all">All</button>
            {{ for status in available_statuses }}
            {{ if status.name != "No Ticket" }}
            <button class="filter-btn" data-status="{{ status.name | string.downcase }}">
                <span>{{ status.icon }}</span> {{ status.name }} ({{ status.count }})
            </button>
            {{ end }}
            {{ end }}
        </div>
        
        <div id="ticket-list">
            <div class="list ticket-groups">
                {{ for ticket in ticket_groups }}
                <div class="ticket-card" id="ticket-{{ ticket.ticket_number }}" data-status="{{ ticket.jira_info ? (ticket.jira_info.status | string.downcase) : 'no-ticket' }}" data-ticket="{{ ticket.ticket_number }}">
                    <div class="ticket-checkbox-outer">
                        <input type="checkbox" class="ticket-checkbox ticket-level" data-ticket="{{ ticket.ticket_number }}" onchange="handleTicketCheckboxChange(this)" />
                    </div>
                    <div class="ticket-content">
                        <div class="ticket-header">
                            <span class="ticket-key name">{{ ticket.ticket_number }}</span>
                            {{ if ticket.jira_info }}
                                <span class="status-badge status" style="background: {{ ticket.status_color }}; color: #fff;">{{ ticket.jira_info.status }}</span>
                            {{ end }}
                            <span class="mr-count">{{ ticket.merge_requests.size }} MRs, {{ ticket.standalone_commits.size }} standalone</span>
                        </div>
                        {{ if ticket.jira_info && ticket.jira_info.summary }}
                        <div class="ticket-summary summary">{{ ticket.jira_info.summary }}</div>
                        {{ end }}
                        {{ if ticket.dependencies.size > 0 || ticket.dependents.size > 0 }}
                        <div class="dependencies">
                            {{ if ticket.dependencies.size > 0 }}
                            <h4>🔗 Dependencies (required first):</h4>
                            <div class="dependency-list">
                                {{ for dep in ticket.dependencies }}
                                <div class="dependency-item" onclick="scrollToTicket('{{ dep.ticket_number }}')" style="background: {{ dep.status_color }}20; color: {{ dep.status_color }};">
                                    {{ dep.ticket_number }} ({{ dep.status }})
                                </div>
                                {{ end }}
                            </div>
                            {{ end }}
                            {{ if ticket.dependents.size > 0 }}
                            <h4>📋 Dependents (depend on this):</h4>
                            <div class="dependency-list">
                                {{ for dep in ticket.dependents }}
                                <div class="dependency-item dependent" onclick="scrollToTicket('{{ dep.ticket_number }}')" style="background: {{ dep.status_color }}20; color: {{ dep.status_color }};">
                                    {{ dep.ticket_number }} ({{ dep.status }})
                                </div>
                                {{ end }}
                            </div>
                            {{ end }}
                        </div>
                        {{ end }}
                        <div class="commit-list">
                            {{ for mr in ticket.merge_requests }}
                            <div class="mr-block">
                                <div class="commit mr-commit">
                                    <input type="checkbox" class="mr-checkbox" data-ticket="{{ ticket.ticket_number }}" data-mr="{{ mr.merge_commit.short_sha }}" onchange="handleMrCheckboxChange(this)" />
                                    {{ if mr.merge_commit.status != "Unknown" }}
                                    <span class="status-badge" style="background: {{ mr.merge_commit.status_color }}; color: #fff; margin-right: 8px;">{{ mr.merge_commit.status }}</span>
                                    {{ end }}
                                    <div class="row1"><span class="icon">🔀</span><span class="sha">{{ mr.merge_commit.short_sha }}</span><span class="message">{{ mr.merge_commit.message }}</span></div>
                                    <div class="row2"><span class="author">{{ mr.merge_commit.author }}</span> | <span class="date">{{ mr.merge_commit.date }}</span></div>
                                </div>
                                {{ for commit in mr.mr_commits }}
                                <div class="commit child">
                                    <input type="checkbox" class="commit-checkbox" data-ticket="{{ ticket.ticket_number }}" data-mr="{{ mr.merge_commit.short_sha }}" data-commit="{{ commit.short_sha }}" onchange="handleCommitCheckboxChange(this)" />
                                    {{ if commit.status != "Unknown" }}
                                    <span class="status-badge" style="background: {{ commit.status_color }}; color: #fff; margin-right: 8px;">{{ commit.status }}</span>
                                    {{ end }}
                                    <div class="row1"><span class="icon">📝</span><span class="sha">{{ commit.short_sha }}</span><span class="message">{{ commit.message }}</span></div>
                                    <div class="row2"><span class="author">{{ commit.author }}</span> | <span class="date">{{ commit.date }}</span></div>
                                </div>
                                {{ end }}
                            </div>
                            {{ end }}
                            {{ for commit in ticket.standalone_commits }}
                            <div class="commit">
                                <input type="checkbox" class="commit-checkbox" data-ticket="{{ ticket.ticket_number }}" data-commit="{{ commit.short_sha }}" onchange="handleCommitCheckboxChange(this)" />
                                {{ if commit.status != "Unknown" }}
                                <span class="status-badge" style="background: {{ commit.status_color }}; color: #fff; margin-right: 8px;">{{ commit.status }}</span>
                                {{ end }}
                                <div class="row1"><span class="icon">📝</span><span class="sha">{{ commit.short_sha }}</span><span class="message">{{ commit.message }}</span></div>
                                <div class="row2"><span class="author">{{ commit.author }}</span> | <span class="date">{{ commit.date }}</span></div>
                            </div>
                            {{ end }}
                        </div>
                    </div>
                </div>
                {{ end }}
            </div>
            <ul class="pagination"></ul>
        </div>
        
        <div class="commands-panel">
            <h3>🍒 Cherry-Pick Commands</h3>
            <div class="command-list" id="commands">Select tickets above to generate cherry-pick commands</div>
            <button onclick="copyCommands()" class="copy-btn">Copy Commands</button>
        </div>
    </div>
    
    <script>
        // Selection state model
        let selected = {
            tickets: new Set(), // ticket_number
            mrs: new Set(),     // mr short_sha
            commits: new Set()  // commit short_sha
        };

        // Helper: get all checkboxes for a ticket
        function getTicketCheckboxes(ticketNumber) {
            const card = document.querySelector(`.ticket-card[data-ticket='${ticketNumber}']`);
            return {
                ticket: card.querySelector('.ticket-level'),
                mrs: Array.from(card.querySelectorAll('.mr-checkbox')),
                commits: Array.from(card.querySelectorAll('.commit-checkbox'))
            };
        }

        // Helper: get all commit checkboxes for an MR
        function getMrCommitCheckboxes(ticketNumber, mrSha) {
            const card = document.querySelector(`.ticket-card[data-ticket='${ticketNumber}']`);
            return Array.from(card.querySelectorAll(`.commit-checkbox[data-mr='${mrSha}']`));
        }

        // --- Checkbox Handlers ---
        function handleTicketCheckboxChange(checkbox) {
            const ticketNumber = checkbox.dataset.ticket;
            const {mrs, commits} = getTicketCheckboxes(ticketNumber);
            if (checkbox.checked) {
                selected.tickets.add(ticketNumber);
                mrs.forEach(cb => { cb.checked = true; selected.mrs.add(cb.dataset.mr); });
                commits.forEach(cb => { cb.checked = true; selected.commits.add(cb.dataset.commit); });
            } else {
                selected.tickets.delete(ticketNumber);
                mrs.forEach(cb => { cb.checked = false; selected.mrs.delete(cb.dataset.mr); });
                commits.forEach(cb => { cb.checked = false; selected.commits.delete(cb.dataset.commit); });
            }
            updateTicketCheckboxState(ticketNumber);
            updateCommands();
        }

        function handleMrCheckboxChange(checkbox) {
            const ticketNumber = checkbox.dataset.ticket;
            const mrSha = checkbox.dataset.mr;
            const commitCheckboxes = getMrCommitCheckboxes(ticketNumber, mrSha);
            if (checkbox.checked) {
                selected.mrs.add(mrSha);
                commitCheckboxes.forEach(cb => { cb.checked = true; selected.commits.add(cb.dataset.commit); });
            } else {
                selected.mrs.delete(mrSha);
                commitCheckboxes.forEach(cb => { cb.checked = false; selected.commits.delete(cb.dataset.commit); });
            }
            updateMrCheckboxState(ticketNumber, mrSha);
            updateTicketCheckboxState(ticketNumber);
            updateCommands();
        }

        function handleCommitCheckboxChange(checkbox) {
            const ticketNumber = checkbox.dataset.ticket;
            const mrSha = checkbox.dataset.mr;
            const commitSha = checkbox.dataset.commit;
            if (checkbox.checked) {
                selected.commits.add(commitSha);
            } else {
                selected.commits.delete(commitSha);
            }
            if (mrSha) updateMrCheckboxState(ticketNumber, mrSha);
            updateTicketCheckboxState(ticketNumber);
            updateCommands();
        }

        // --- State Updaters ---
        function updateTicketCheckboxState(ticketNumber) {
            const {ticket, mrs, commits} = getTicketCheckboxes(ticketNumber);
            const allChecked = mrs.every(cb => cb.checked) && commits.every(cb => cb.checked);
            ticket.checked = allChecked;
            if (allChecked) {
                selected.tickets.add(ticketNumber);
            } else {
                selected.tickets.delete(ticketNumber);
            }
        }
        function updateMrCheckboxState(ticketNumber, mrSha) {
            const card = document.querySelector(`.ticket-card[data-ticket='${ticketNumber}']`);
            const mrCheckbox = card.querySelector(`.mr-checkbox[data-mr='${mrSha}']`);
            const commitCheckboxes = getMrCommitCheckboxes(ticketNumber, mrSha);
            const allChecked = commitCheckboxes.length > 0 && commitCheckboxes.every(cb => cb.checked);
            mrCheckbox.checked = allChecked;
            if (allChecked) {
                selected.mrs.add(mrSha);
            } else {
                selected.mrs.delete(mrSha);
            }
        }

        // --- Bulk Actions ---
        function selectAll() {
            document.querySelectorAll('.ticket-card').forEach(card => {
                if (card.style.display !== 'none') {
                    const ticketCheckbox = card.querySelector('.ticket-level');
                    if (ticketCheckbox) {
                        ticketCheckbox.checked = true;
                        handleTicketCheckboxChange(ticketCheckbox);
                    }
                }
            });
        }
        function clearSelection() {
            document.querySelectorAll('.ticket-card').forEach(card => {
                if (card.style.display !== 'none') {
                    const ticketCheckbox = card.querySelector('.ticket-level');
                    if (ticketCheckbox) {
                        ticketCheckbox.checked = false;
                        handleTicketCheckboxChange(ticketCheckbox);
                    }
                }
            });
        }
        function selectReady() {
            document.querySelectorAll('.ticket-card').forEach(card => {
                if (card.style.display !== 'none') {
                    const status = card.querySelector('.status');
                    const ticketCheckbox = card.querySelector('.ticket-level');
                    if (status && ticketCheckbox) {
                        const statusText = status.textContent.toLowerCase();
                        if (statusText.includes('done') || statusText.includes('deployed') || statusText.includes('ready')) {
                            ticketCheckbox.checked = true;
                            handleTicketCheckboxChange(ticketCheckbox);
                        } else {
                            ticketCheckbox.checked = false;
                            handleTicketCheckboxChange(ticketCheckbox);
                        }
                    }
                }
            });
        }

        // --- Filter/Search State Preservation ---
        // (No-op: selection state is in JS, checkboxes reflect it on DOM update)

        // --- Command Generation ---
        function updateCommands() {
            const commandsEl = document.getElementById('commands');
            let commands = ['git checkout {{ target_branch }}'];
            let selectedItems = [];
            let selectedMrShas = new Set();

            // First, collect all checked MRs
            document.querySelectorAll('.mr-checkbox:checked').forEach(cb => {
                selectedMrShas.add(cb.dataset.mr);
            });

            // Then, collect all checked MR and commit checkboxes in DOM order
            document.querySelectorAll('.mr-checkbox:checked, .commit-checkbox:checked').forEach(cb => {
                let sha, type, ticket, message, parentMr;
                if (cb.classList.contains('mr-checkbox')) {
                    sha = cb.dataset.mr;
                    type = 'mr';
                    parentMr = null;
                } else {
                    sha = cb.dataset.commit;
                    type = 'commit';
                    parentMr = cb.dataset.mr || null;
                }
                // If this is a commit and its parent MR is selected, skip it
                if (type === 'commit' && parentMr && selectedMrShas.has(parentMr)) return;

                // Find parent ticket card
                const card = cb.closest('.ticket-card');
                ticket = card ? card.dataset.ticket : '';
                // Find commit message
                const msgEl = cb.parentElement.querySelector('.message');
                message = msgEl ? msgEl.textContent.trim() : '';
                selectedItems.push({sha, type, ticket, message});
            });

            // Remove duplicates
            const seen = new Set();
            selectedItems = selectedItems.filter(item => {
                if (seen.has(item.sha)) return false;
                seen.add(item.sha);
                return true;
            });

            // Build commands with comments
            selectedItems.forEach(item => {
                if (item.sha) {
                    commands.push(`# ${item.ticket}: ${item.message}`);
                    if (item.type === 'mr') {
                        commands.push(`git cherry-pick -m 1 ${item.sha}`);
                    } else {
                        commands.push(`git cherry-pick ${item.sha}`);
                    }
                }
            });
            commandsEl.textContent = commands.join('\n');
        }

        // --- Multi-Filter Selection Logic ---
        let activeFilters = new Set();
        function updateFilterBar() {
            document.querySelectorAll('.filter-btn').forEach(btn => {
                const status = btn.getAttribute('data-status');
                if (status === 'all') {
                    btn.classList.toggle('active', activeFilters.size === 0);
                } else {
                    btn.classList.toggle('active', activeFilters.has(status));
                }
            });
        }
        function applyFilters() {
            document.querySelectorAll('.ticket-card').forEach(card => {
                const status = (card.getAttribute('data-status') || '').toLowerCase();
                if (activeFilters.size === 0 || Array.from(activeFilters).some(f => f.toLowerCase() === status)) {
                    card.style.display = '';
                } else {
                    card.style.display = 'none';
                }
            });
        }
        document.querySelectorAll('.filter-btn').forEach(btn => {
            btn.addEventListener('click', function() {
                const status = btn.getAttribute('data-status');
                if (status === 'all') {
                    activeFilters.clear();
                } else {
                    if (activeFilters.has(status)) {
                        activeFilters.delete(status);
                    } else {
                        activeFilters.add(status);
                    }
                }
                updateFilterBar();
                applyFilters();
            });
        });
        // On load, ensure filter bar is correct
        updateFilterBar();
        applyFilters();

        // --- List.js and Filter Integration ---
        // (Removed List.js filter button click handlers to avoid conflicts with custom multi-filter logic)

        // --- Utility: Scroll to Ticket ---
        function scrollToTicket(ticketNumber) {
            const element = document.getElementById('ticket-' + ticketNumber);
            if (element) {
                element.scrollIntoView({ behavior: 'smooth', block: 'center' });
                // Optionally highlight the ticket briefly
                element.style.boxShadow = '0 0 0 3px #3b82f6';
                setTimeout(() => {
                    element.style.boxShadow = '';
                }, 2000);
            }
        }
    </script>
</body>
</html>
""";

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
            var uniqueDependencies = dependencies.GetValueOrDefault(ticketGroup.TicketNumber, new List<string>()).Distinct().ToList();
            var uniqueDependents = dependencies.Where(kvp => kvp.Value.Contains(ticketGroup.TicketNumber))
                .Select(kvp => kvp.Key)
                .Distinct()
                .ToList();

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
                        date = mr.MergeCommit.Date.ToString("yyyy-MM-dd"),
                        status = ticketGroup.JiraInfo?.Status ?? "Unknown",
                        status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280"
                    },
                    mr_commits = mr.MrCommits.Select(c => new
                    {
                        short_sha = c.ShortSha,
                        message = c.Message,
                        author = c.Author,
                        date = c.Date.ToString("yyyy-MM-dd"),
                        status = ticketGroup.JiraInfo?.Status ?? "Unknown",
                        status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280"
                    }).ToList()
                }).ToList(),
                standalone_commits = ticketGroup.StandaloneCommits.Select(c => new
                {
                    short_sha = c.ShortSha,
                    message = c.Message,
                    author = c.Author,
                    date = c.Date.ToString("yyyy-MM-dd"),
                    status = ticketGroup.JiraInfo?.Status ?? "Unknown",
                    status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280"
                }).ToList(),
                dependencies = uniqueDependencies
                    .Where(dep => ticketLookup.ContainsKey(dep))
                    .Select(dep => new
                    {
                        ticket_number = dep,
                        status = ticketLookup[dep].JiraInfo?.Status ?? "Unknown",
                        status_color = ticketLookup[dep].JiraInfo != null ? GetStatusColor(ticketLookup[dep].JiraInfo.Status) : "#6b7280"
                    }).ToList(),
                dependents = uniqueDependents
                    .Where(dep => ticketLookup.ContainsKey(dep) && !uniqueDependencies.Contains(dep))
                    .Select(dep => new
                    {
                        ticket_number = dep,
                        status = ticketLookup[dep].JiraInfo?.Status ?? "Unknown",
                        status_color = ticketLookup[dep].JiraInfo != null ? GetStatusColor(ticketLookup[dep].JiraInfo.Status) : "#6b7280"
                    }).ToList()
            };
            
            result.Add(ticketData);
        }
        
        return result;
    }
    
    private Dictionary<string, List<string>> AnalyzeTicketDependencies(List<TicketGroup> ticketGroups)
    {
        var dependencies = new Dictionary<string, List<string>>();
        var shaToTickets = new Dictionary<string, List<string>>();

        // Build SHA-to-ticket map
        foreach (var group in ticketGroups)
        {
            foreach (var mr in group.MergeRequests)
            {
                if (!shaToTickets.ContainsKey(mr.MergeCommit.Sha))
                    shaToTickets[mr.MergeCommit.Sha] = new List<string>();
                shaToTickets[mr.MergeCommit.Sha].Add(group.TicketNumber);
                foreach (var c in mr.MrCommits)
                {
                    if (!shaToTickets.ContainsKey(c.Sha))
                        shaToTickets[c.Sha] = new List<string>();
                    shaToTickets[c.Sha].Add(group.TicketNumber);
                }
            }
            foreach (var c in group.StandaloneCommits)
            {
                if (!shaToTickets.ContainsKey(c.Sha))
                    shaToTickets[c.Sha] = new List<string>();
                shaToTickets[c.Sha].Add(group.TicketNumber);
            }
        }

        // For each ticket, find dependencies by SHA overlap
        foreach (var group in ticketGroups)
        {
            var ticketDeps = new HashSet<string>();
            foreach (var mr in group.MergeRequests)
            {
                foreach (var sha in new[] { mr.MergeCommit.Sha }.Concat(mr.MrCommits.Select(c => c.Sha)))
                {
                    foreach (var ticket in shaToTickets[sha])
                    {
                        if (ticket != group.TicketNumber)
                            ticketDeps.Add(ticket);
                    }
                }
            }
            foreach (var c in group.StandaloneCommits)
            {
                foreach (var ticket in shaToTickets[c.Sha])
                {
                    if (ticket != group.TicketNumber)
                        ticketDeps.Add(ticket);
                }
            }
            if (ticketDeps.Any())
                dependencies[group.TicketNumber] = ticketDeps.ToList();
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
            _ => 50
        };
    }
} 