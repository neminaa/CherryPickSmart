using System.Text.RegularExpressions;
using CherryPickAnalyzer.Models;
using Scriban;

namespace CherryPickAnalyzer.Services;

public partial class HtmlExportService
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
        .header .repo-name { margin: 8px 0; color: #2563eb; font-size: 1.1em; }
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
        
        /* Results summary */
        .results-summary {
            display: flex;
            gap: 24px;
            margin-bottom: 16px;
            padding: 12px 16px;
            background: #f8fafc;
            border-radius: 8px;
            border-left: 3px solid #3b82f6;
        }
        .summary-item {
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .summary-item .label {
            font-weight: 500;
            color: #374151;
        }
        .summary-item .value {
            font-weight: 600;
            color: #2563eb;
        }
        
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
        
        /* Link styling */
        .commit a.sha:hover, .ticket-key.name:hover, .repo-name a:hover { 
            text-decoration: underline !important; 
            color: #1d4ed8 !important; 
        }
        
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
        .command-count {
            font-size: 0.8em;
            color: #64748b;
            font-weight: 400;
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

        .dependency-warning {
            background: #fffbe6;
            color: #b45309;
            border: 1px solid #fde68a;
            border-radius: 8px;
            padding: 16px 20px;
            margin-bottom: 24px;
            font-size: 1.05em;
            box-shadow: 0 2px 8px rgba(255, 193, 7, 0.08);
        }
        .dependency-warning strong {
            color: #92400e;
        }
        .dependency-warning ul {
            margin: 8px 0 0 24px;
        }
    </style>
    <!-- Removed List.js - using custom filtering and pagination -->
</head>
<body>
    <div class="container">
        <div class="header">
            <h1><span class="icon">🍒</span> Cherry-Pick Analysis</h1>
            {{ if repository_name }}
            <p class="repo-name">
                {{ if repository_url }}
                    <a href="{{ repository_url }}" target="_blank" style="text-decoration: none; color: #2563eb;">
                        <strong>📦 {{ repository_name }}</strong>
                    </a>
                {{ else }}
                    <strong>📦 {{ repository_name }}</strong>
                {{ end }}
            </p>
            {{ end }}
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
            <input class="search" placeholder="Search tickets, status, summary, commit SHAs, messages..." />
        </div>
        
        <div class="selection-controls">
            <button class="selection-btn" onclick="selectAll()">Select All</button>
            <button class="selection-btn" onclick="clearSelection()">Clear Selection</button>
        </div>
        
        <div class="filter-bar" id="filter-bar">
            <button class="filter-btn active" data-status="all">All</button>
            {{ for status in available_statuses }}
            <button class="filter-btn" data-status="{{ status.name }}">
                <span>{{ status.icon }}</span> {{ status.display_name }} ({{ status.count }})
            </button>
            {{ end }}
        </div>
        
        <div class="results-summary" id="results-summary">
            <div class="summary-item">
                <span class="label">Showing:</span>
                <span class="value" id="showing-count">{{ ticket_groups.size }} tickets</span>
            </div>
            <div class="summary-item">
                <span class="label">Selected:</span>
                <span class="value" id="selected-count">0 tickets</span>
            </div>
        </div>
        
        <div id="ticket-list">
            <div class="list ticket-groups">
                {{ for ticket in ticket_groups }}
                <div class="ticket-card" id="ticket-{{ ticket.ticket_number }}" data-status="{{ ticket.jira_info ? (ticket.jira_info.status | string.downcase | string.replace ' ' '-') : 'no-ticket' }}" data-ticket="{{ ticket.ticket_number }}">
                    <div class="ticket-checkbox-outer">
                        <input type="checkbox" class="ticket-checkbox ticket-level" data-ticket="{{ ticket.ticket_number }}" onchange="handleTicketCheckboxChange(this)" />
                    </div>
                    <div class="ticket-content">
                        <div class="ticket-header">
                            {{ if ticket.jira_link }}
                                <a href="{{ ticket.jira_link }}" target="_blank" class="ticket-key name" style="text-decoration: none; color: #2563eb;">{{ ticket.ticket_number }}</a>
                            {{ else }}
                                <span class="ticket-key name">{{ ticket.ticket_number }}</span>
                            {{ end }}
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
                                <div class="dependency-item" data-ticket="{{ dep.ticket_number }}" onclick="scrollToTicket('{{ dep.ticket_number }}')" style="background: {{ dep.status_color }}20; color: {{ dep.status_color }};">
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
                                    <div class="row1">
                                        <span class="icon">🔀</span>
                                        {{ if mr.merge_commit.commit_link }}
                                            <a href="{{ mr.merge_commit.commit_link }}" target="_blank" class="sha" style="text-decoration: none; color: #2563eb;">{{ mr.merge_commit.short_sha }}</a>
                                        {{ else }}
                                            <span class="sha">{{ mr.merge_commit.short_sha }}</span>
                                        {{ end }}
                                        <span class="message">{{ mr.merge_commit.message }}</span>
                                    </div>
                                    <div class="row2"><span class="author">{{ mr.merge_commit.author }}</span> | <span class="date">{{ mr.merge_commit.date }}</span></div>
                                </div>
                                {{ for commit in mr.mr_commits }}
                                <div class="commit child">
                                    <input type="checkbox" class="commit-checkbox" data-ticket="{{ ticket.ticket_number }}" data-mr="{{ mr.merge_commit.short_sha }}" data-commit="{{ commit.short_sha }}" onchange="handleCommitCheckboxChange(this)" />
                                    <div class="row1">
                                        <span class="icon">📝</span>
                                        {{ if commit.commit_link }}
                                            <a href="{{ commit.commit_link }}" target="_blank" class="sha" style="text-decoration: none; color: #2563eb;">{{ commit.short_sha }}</a>
                                        {{ else }}
                                            <span class="sha">{{ commit.short_sha }}</span>
                                        {{ end }}
                                        <span class="message">{{ commit.message }}</span>
                                    </div>
                                    <div class="row2"><span class="author">{{ commit.author }}</span> | <span class="date">{{ commit.date }}</span></div>
                                </div>
                                {{ end }}
                            </div>
                            {{ end }}
                            {{ for commit in ticket.standalone_commits }}
                            <div class="commit">
                                <input type="checkbox" class="commit-checkbox" data-ticket="{{ ticket.ticket_number }}" data-commit="{{ commit.short_sha }}" onchange="handleCommitCheckboxChange(this)" />
                                <div class="row1">
                                    <span class="icon">📝</span>
                                    {{ if commit.commit_link }}
                                        <a href="{{ commit.commit_link }}" target="_blank" class="sha" style="text-decoration: none; color: #2563eb;">{{ commit.short_sha }}</a>
                                    {{ else }}
                                        <span class="sha">{{ commit.short_sha }}</span>
                                    {{ end }}
                                    <span class="message">{{ commit.message }}</span>
                                </div>
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
        
        <div id="dependency-warning" class="dependency-warning" style="display:none;"></div>

        <div class="commands-panel">
            <h3>🍒 Cherry-Pick Commands <span class="command-count" id="command-count">(0 commands)</span></h3>
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
            // Select all checkboxes for all filtered tickets (not just visible ones)
            filteredTickets.forEach(card => {
                const ticketCheckbox = card.querySelector('.ticket-level');
                if (ticketCheckbox && !ticketCheckbox.checked) {
                    ticketCheckbox.checked = true;
                    handleTicketCheckboxChange(ticketCheckbox);
                }
            });
            
            // Update commands and warnings after selecting all
            updateCommands();
        }
        function clearSelection() {
            // Uncheck all checkboxes for all filtered tickets (not just visible ones)
            filteredTickets.forEach(card => {
                const ticketCheckbox = card.querySelector('.ticket-level');
                if (ticketCheckbox && ticketCheckbox.checked) {
                    ticketCheckbox.checked = false;
                    handleTicketCheckboxChange(ticketCheckbox);
                }
            });
            
            // Clear the commands section and hide the warning
            const commandsEl = document.getElementById('commands');
            if (commandsEl) {
                commandsEl.textContent = 'Select tickets above to generate cherry-pick commands';
            }
            
            // Update command count to show 0
            updateCommandCount(0);
            
            // Clear the dependency warning
            const warningEl = document.getElementById('dependency-warning');
            if (warningEl) {
                warningEl.innerHTML = '';
                warningEl.style.display = 'none';
            }
            
            // Update selected count to show 0
            const selectedCountEl = document.getElementById('selected-count');
            if (selectedCountEl) {
                selectedCountEl.textContent = '0 tickets';
            }
        }


        // --- Filter/Search State Preservation ---
        // (No-op: selection state is in JS, checkboxes reflect it on DOM update)

        // --- Command Generation ---
        function updateCommands() {
            const commandsEl = document.getElementById('commands');
            let commands = ['git checkout {{ target_branch }}'];
            let selectedItems = [];
            let selectedMrShas = new Set();
            let selectedTicketNumbers = new Set();

            // Collect all checked ticket numbers
            document.querySelectorAll('.ticket-level:checked').forEach(cb => {
                selectedTicketNumbers.add(cb.dataset.ticket);
            });

            // Collect all checked MRs not under a selected ticket
            document.querySelectorAll('.mr-checkbox:checked').forEach(cb => {
                const ticket = cb.dataset.ticket;
                if (!selectedTicketNumbers.has(ticket)) {
                    selectedMrShas.add(cb.dataset.mr);
                }
            });

            // Group selected items by ticket
            let ticketGroups = {};

            // Add selected tickets (all MRs/commits under them are implicitly selected)
            selectedTicketNumbers.forEach(ticket => {
                if (!ticketGroups[ticket]) ticketGroups[ticket] = {mrs: [], commits: []};
                // Add all checked MRs under this ticket
                document.querySelectorAll(`.mr-checkbox[data-ticket='${ticket}']`).forEach(cb => {
                    ticketGroups[ticket].mrs.push(cb.dataset.mr);
                });
                // Add all checked commits under this ticket
                document.querySelectorAll(`.commit-checkbox[data-ticket='${ticket}']`).forEach(cb => {
                    ticketGroups[ticket].commits.push(cb.dataset.commit);
                });
            });

            // Add selected MRs (not under a selected ticket)
            selectedMrShas.forEach(mrSha => {
                const cb = document.querySelector(`.mr-checkbox[data-mr='${mrSha}']`);
                if (!cb) return;
                const ticket = cb.dataset.ticket;
                if (!ticketGroups[ticket]) ticketGroups[ticket] = {mrs: [], commits: []};
                ticketGroups[ticket].mrs.push(mrSha);
                // Add all checked commits under this MR (not under a selected ticket)
                document.querySelectorAll(`.commit-checkbox[data-mr='${mrSha}'][data-ticket='${ticket}']`).forEach(commitCb => {
                    ticketGroups[ticket].commits.push(commitCb.dataset.commit);
                });
            });

            // Add selected commits (not under a selected ticket or selected MR)
            document.querySelectorAll('.commit-checkbox:checked').forEach(cb => {
                const ticket = cb.dataset.ticket;
                const mr = cb.dataset.mr;
                if (!selectedTicketNumbers.has(ticket) && (!mr || !selectedMrShas.has(mr))) {
                    if (!ticketGroups[ticket]) ticketGroups[ticket] = {mrs: [], commits: []};
                    ticketGroups[ticket].commits.push(cb.dataset.commit);
                }
            });

            // Now, for each ticket, output the header and commands
            let ticketIndex = 1;
            Object.keys(ticketGroups).sort().forEach(ticket => {
                const group = ticketGroups[ticket];
                // Remove duplicates
                group.mrs = [...new Set(group.mrs)];
                group.commits = [...new Set(group.commits)];
                // Header
                const visibleCommits = group.commits.filter(commitSha => {
                    const cb = document.querySelector(`.commit-checkbox[data-commit='${commitSha}'][data-ticket='${ticket}']`);
                    if (!cb) return false;
                    const parentMr = cb.dataset.mr;
                    if (parentMr && group.mrs.includes(parentMr)) return false;
                    return true;
                });
                commands.push(`# ${ticketIndex} ${ticket} (${group.mrs.length} MR${group.mrs.length !== 1 ? 's' : ''}, ${visibleCommits.length} commit${visibleCommits.length !== 1 ? 's' : ''})`);
                // For each MR, output the cherry-pick command
                group.mrs.forEach(mrSha => {
                    const cb = document.querySelector(`.mr-checkbox[data-mr='${mrSha}']`);
                    if (!cb) return;
                    const card = cb.closest('.ticket-card');
                    const ticketNum = card ? card.dataset.ticket : '';
                    const msgEl = cb.parentElement.querySelector('.message');
                    const message = msgEl ? msgEl.textContent.trim() : '';
                    commands.push(`# ${ticketNum}: ${message}`);
                    commands.push(`git cherry-pick -m 1 ${mrSha}`);
                });
                // For each commit, output the cherry-pick command
                visibleCommits.forEach(commitSha => {
                    const cb = document.querySelector(`.commit-checkbox[data-commit='${commitSha}'][data-ticket='${ticket}']`);
                    if (!cb) return;
                    const card = cb.closest('.ticket-card');
                    const ticketNum = card ? card.dataset.ticket : '';
                    const msgEl = cb.parentElement.querySelector('.message');
                    const message = msgEl ? msgEl.textContent.trim() : '';
                    const parentMr = cb.dataset.mr;
                    if (parentMr && group.mrs.includes(parentMr)) return;
                    commands.push(`# ${ticketNum}: ${message}`);
                    commands.push(`git cherry-pick ${commitSha}`);
                });
                ticketIndex++;
                commands.push(''); // Blank line between tickets
            });

            commandsEl.textContent = commands.join('\n');
            
            // Update command count
            let totalCommands = 0;
            Object.keys(ticketGroups).forEach(ticket => {
                const group = ticketGroups[ticket];
                const visibleCommits = group.commits.filter(commitSha => {
                    const cb = document.querySelector(`.commit-checkbox[data-commit='${commitSha}'][data-ticket='${ticket}']`);
                    if (!cb) return false;
                    const parentMr = cb.dataset.mr;
                    if (parentMr && group.mrs.includes(parentMr)) return false;
                    return true;
                });
                totalCommands += group.mrs.length + visibleCommits.length;
            });
            updateCommandCount(totalCommands);
            
            // Update selected count
            updateSelectedCount();

            updateDependencyWarning();
        }
        
        function updateCommandCount(count) {
            const commandCountEl = document.getElementById('command-count');
            if (commandCountEl) {
                commandCountEl.textContent = `(${count} command${count !== 1 ? 's' : ''})`;
            }
        }
        
        function updateSelectedCount() {
            const selectedCountEl = document.getElementById('selected-count');
            if (!selectedCountEl) return;

            // Get all checked ticket numbers
            const selectedTicketNumbers = new Set(
                Array.from(document.querySelectorAll('.ticket-level:checked')).map(cb => cb.dataset.ticket)
            );

            // Get all checked MR shas (that are not under a selected ticket)
            const selectedMrShas = new Set();
            document.querySelectorAll('.mr-checkbox:checked').forEach(cb => {
                const ticket = cb.dataset.ticket;
                if (!selectedTicketNumbers.has(ticket)) {
                    selectedMrShas.add(cb.dataset.mr);
                }
            });

            // Get all checked commits (that are not under a selected ticket or selected MR)
            let selectedCommitCount = 0;
            document.querySelectorAll('.commit-checkbox:checked').forEach(cb => {
                const ticket = cb.dataset.ticket;
                const mr = cb.dataset.mr;
                if (!selectedTicketNumbers.has(ticket) && (!mr || !selectedMrShas.has(mr))) {
                    selectedCommitCount++;
                }
            });

            const ticketCount = selectedTicketNumbers.size;
            const mrCount = selectedMrShas.size;
            const commitCount = selectedCommitCount;

            selectedCountEl.textContent =
                `${ticketCount} ticket${ticketCount !== 1 ? 's' : ''}, ` +
                `${mrCount} MR${mrCount !== 1 ? 's' : ''}, ` +
                `${commitCount} commit${commitCount !== 1 ? 's' : ''}`;
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
            // This function is now handled by applySearchAndFilters()
            applySearchAndFilters();
        }

        // --- Custom Filtering and Pagination System ---
        let currentPage = 1;
        const itemsPerPage = 10;
        let filteredTickets = [];
        
        function initializeCustomSystem() {
            // Get all ticket cards
            const allTickets = Array.from(document.querySelectorAll('.ticket-card'));
            filteredTickets = allTickets;
            
            // Set up search functionality
            const searchInput = document.querySelector('.search');
            if (searchInput) {
                searchInput.addEventListener('input', function() {
                    applySearchAndFilters();
                });
            }
            
            // Set up filter button functionality
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
            
            // Initial setup
            updateFilterBar();
            renderPagination();
            updateDisplay();
        }
        
        function applySearchAndFilters() {
            const searchTerm = document.querySelector('.search').value.toLowerCase();
            const allTickets = Array.from(document.querySelectorAll('.ticket-card'));
            
            // Apply both search and status filters
            filteredTickets = allTickets.filter(card => {
                const status = (card.getAttribute('data-status') || '').toLowerCase();
                const ticketNumber = card.querySelector('.ticket-key')?.textContent?.toLowerCase() || '';
                const summary = card.querySelector('.ticket-summary')?.textContent?.toLowerCase() || '';
                
                // Check status filter
                const statusMatch = activeFilters.size === 0 || Array.from(activeFilters).some(f => f.toLowerCase() === status);
                
                // Check search filter - include commit SHAs and messages
                let searchMatch = !searchTerm || 
                    ticketNumber.includes(searchTerm) || 
                    summary.includes(searchTerm) ||
                    status.includes(searchTerm);
                
                // If no match found in ticket info, search through commits
                if (!searchMatch && searchTerm) {
                    // Search through all commit SHAs and messages in this ticket
                    const commitElements = card.querySelectorAll('.commit');
                    for (const commitEl of commitElements) {
                        const sha = commitEl.querySelector('.sha')?.textContent?.toLowerCase() || '';
                        const message = commitEl.querySelector('.message')?.textContent?.toLowerCase() || '';
                        
                        if (sha.includes(searchTerm) || message.includes(searchTerm)) {
                            searchMatch = true;
                            break;
                        }
                    }
                }
                
                return statusMatch && searchMatch;
            });
            
            currentPage = 1; // Reset to first page when filtering
            renderPagination();
            updateDisplay();
        }
        
        function updateDisplay() {
            const allTickets = document.querySelectorAll('.ticket-card');
            const startIndex = (currentPage - 1) * itemsPerPage;
            const endIndex = startIndex + itemsPerPage;
            
            // First, hide all tickets
            allTickets.forEach(card => {
                card.style.display = 'none';
            });
            
            // Then, show only the tickets that are in the current page of filtered results
            filteredTickets.slice(startIndex, endIndex).forEach(card => {
                card.style.display = '';
            });
            
            // Update the "Showing" count
            updateShowingCount();
            updateDependencyWarning();
        }
        
        function updateShowingCount() {
            const showingCountEl = document.getElementById('showing-count');
            if (showingCountEl) {
                const count = filteredTickets.length;
                showingCountEl.textContent = `${count} ticket${count !== 1 ? 's' : ''}`;
            }
        }
        
        function renderPagination() {
            const paginationContainer = document.querySelector('.pagination');
            if (!paginationContainer) return;
            
            const totalPages = Math.ceil(filteredTickets.length / itemsPerPage);
            
            if (totalPages <= 1) {
                paginationContainer.innerHTML = '';
                return;
            }
            
            let paginationHTML = '';
            
            // Previous button
            if (currentPage > 1) {
                paginationHTML += `<li><a href="#" onclick="goToPage(${currentPage - 1})">← Previous</a></li>`;
            }
            
            // Page numbers
            const startPage = Math.max(1, currentPage - 2);
            const endPage = Math.min(totalPages, currentPage + 2);
            
            for (let i = startPage; i <= endPage; i++) {
                if (i === currentPage) {
                    paginationHTML += `<li class="active"><a href="#">${i}</a></li>`;
                } else {
                    paginationHTML += `<li><a href="#" onclick="goToPage(${i})">${i}</a></li>`;
                }
            }
            
            // Next button
            if (currentPage < totalPages) {
                paginationHTML += `<li><a href="#" onclick="goToPage(${currentPage + 1})">Next →</a></li>`;
            }
            
            paginationContainer.innerHTML = paginationHTML;
        }
        
        function goToPage(page) {
            currentPage = page;
            updateDisplay();
            renderPagination();
        }
        
        // Initialize when DOM is ready
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', initializeCustomSystem);
        } else {
            initializeCustomSystem();
        }

        // --- Utility: Scroll to Ticket ---
        function scrollToTicket(ticketNumber) {
            const element = document.getElementById('ticket-' + ticketNumber);
            if (!element) return;
            
            // Check if the ticket is currently visible (on current page)
            const isVisible = element.style.display !== 'none';
            
            if (!isVisible) {
                // Find which page this ticket is on
                const ticketIndex = filteredTickets.findIndex(card => card.dataset.ticket === ticketNumber);
                if (ticketIndex !== -1) {
                    const targetPage = Math.floor(ticketIndex / itemsPerPage) + 1;
                    if (targetPage !== currentPage) {
                        // Navigate to the correct page first
                        goToPage(targetPage);
                        // Wait a bit for the page to render, then scroll
                        setTimeout(() => {
                            scrollToTicketOnCurrentPage(ticketNumber);
                        }, 100);
                        return;
                    }
                }
            }
            
            // If we're here, the ticket is on the current page or we're already on the right page
            scrollToTicketOnCurrentPage(ticketNumber);
        }
        
        function scrollToTicketOnCurrentPage(ticketNumber) {
            const element = document.getElementById('ticket-' + ticketNumber);
            if (element) {
                element.scrollIntoView({ behavior: 'smooth', block: 'center' });
                // Highlight the ticket briefly
                element.style.boxShadow = '0 0 0 3px #3b82f6';
                setTimeout(() => {
                    element.style.boxShadow = '';
                }, 2000);
            }
        }

        // --- Copy Commands Function ---
        function copyCommands() {
            const commandsEl = document.getElementById('commands');
            const text = commandsEl.textContent;
            
            if (navigator.clipboard && window.isSecureContext) {
                // Use modern clipboard API
                navigator.clipboard.writeText(text).then(() => {
                    const btn = document.querySelector('.copy-btn');
                    const originalText = btn.textContent;
                    btn.textContent = 'Copied!';
                    btn.style.background = '#10b981';
                    setTimeout(() => {
                        btn.textContent = originalText;
                        btn.style.background = '#2563eb';
                    }, 2000);
                }).catch(err => {
                    console.error('Failed to copy: ', err);
                    fallbackCopyTextToClipboard(text);
                });
            } else {
                // Fallback for older browsers
                fallbackCopyTextToClipboard(text);
            }
        }

        function fallbackCopyTextToClipboard(text) {
            const textArea = document.createElement('textarea');
            textArea.value = text;
            textArea.style.top = '0';
            textArea.style.left = '0';
            textArea.style.position = 'fixed';
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            
            try {
                const successful = document.execCommand('copy');
                if (successful) {
                    const btn = document.querySelector('.copy-btn');
                    const originalText = btn.textContent;
                    btn.textContent = 'Copied!';
                    btn.style.background = '#10b981';
                    setTimeout(() => {
                        btn.textContent = originalText;
                        btn.style.background = '#2563eb';
                    }, 2000);
                }
            } catch (err) {
                console.error('Fallback: Oops, unable to copy', err);
            }
            
            document.body.removeChild(textArea);
        }

        function updateDependencyWarning() {
            const warningEl = document.getElementById('dependency-warning');
            if (!warningEl) return;

            // Get all selected ticket numbers
            const selectedTicketNumbers = new Set(
                Array.from(document.querySelectorAll('.ticket-level:checked')).map(cb => cb.dataset.ticket)
            );

            // For each selected ticket, check its dependencies
            let missingDeps = [];
            selectedTicketNumbers.forEach(ticket => {
                const card = document.querySelector(`.ticket-card[data-ticket='${ticket}']`);
                if (!card) return;
                // Find dependency items in the DOM
                const depItems = card.querySelectorAll('.dependencies .dependency-list:not(.dependents) .dependency-item');
                depItems.forEach(depEl => {
                    const depTicket = depEl.dataset.ticket;
                    if (depTicket && !selectedTicketNumbers.has(depTicket)) {
                        missingDeps.push({ticket, dep: depTicket});
                    }
                });
            });

            if (missingDeps.length > 0) {
                // Group by ticket
                const grouped = {};
                missingDeps.forEach(({ticket, dep}) => {
                    if (!grouped[ticket]) grouped[ticket] = [];
                    grouped[ticket].push(dep);
                });
                let html = '<strong>⚠️ Dependency Warning:</strong> Some selected tickets have dependencies that are not selected. Please select the dependencies first to avoid cherry-pick issues.';
                html += '<ul>';
                Object.keys(grouped).forEach(ticket => {
                    const deps = grouped[ticket];
                    const depLinks = deps.map(dep => `<a href="#" onclick="scrollToTicket('${dep}'); return false;" style="color:#b91c1c; text-decoration: underline; cursor: pointer;">${dep}</a>`).join(', ');
                    html += `<li><strong>${ticket}</strong> is missing dependencies: ${depLinks}</li>`;
                });
                html += '</ul>';
                warningEl.innerHTML = html;
                warningEl.style.display = '';
            } else {
                warningEl.innerHTML = '';
                warningEl.style.display = 'none';
            }
        }
    </script>
</body>
</html>
""";

    public static string GenerateHtml(DeploymentAnalysis analysis, string sourceBranch, string targetBranch, string? repositoryUrl = null, string? jiraBaseUrl = null, string? repositoryName = null)
    {
        var template = Template.Parse(HtmlTemplate);
        
        // Prepare data for template
        var ticketGroups = PrepareTicketGroups(analysis.ContentAnalysis.TicketGroups, repositoryUrl, jiraBaseUrl);
        var context = new
        {
            source_branch = sourceBranch,
            target_branch = targetBranch,
            repository_name = repositoryName,
            repository_url = repositoryUrl,
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
    
    private static List<object> PrepareTicketGroups(List<TicketGroup> ticketGroups, string? repositoryUrl = null, string? jiraBaseUrl = null)
    {
        var result = new List<object>();
        
        // First pass: create ticket lookup and analyze dependencies
        var ticketLookup = ticketGroups.ToDictionary(tg => tg.TicketNumber, tg => tg);
        var dependencies = AnalyzeTicketDependencies(ticketGroups);
        
        foreach (var ticketGroup in ticketGroups)
        {
            var uniqueDependencies = dependencies.GetValueOrDefault(ticketGroup.TicketNumber, []).Distinct().ToList();
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
                jira_link = ticketGroup.TicketNumber != "No Ticket" ? GenerateJiraLink(jiraBaseUrl, ticketGroup.TicketNumber) : null,
                status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280",
                merge_requests = ticketGroup.MergeRequests.Select(mr => new
                {
                    merge_commit = new
                    {
                        short_sha = mr.MergeCommit.ShortSha,
                        sha = mr.MergeCommit.Sha,
                        message = mr.MergeCommit.Message,
                        author = mr.MergeCommit.Author,
                        date = mr.MergeCommit.Date.ToString("yyyy-MM-dd"),
                        status = ticketGroup.JiraInfo?.Status ?? "Unknown",
                        status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280",
                        commit_link = GenerateCommitLink(repositoryUrl, mr.MergeCommit.Sha)
                    },
                    mr_commits = mr.MrCommits.Select(c => new
                    {
                        short_sha = c.ShortSha,
                        sha = c.Sha,
                        message = c.Message,
                        author = c.Author,
                        date = c.Date.ToString("yyyy-MM-dd"),
                        status = ticketGroup.JiraInfo?.Status ?? "Unknown",
                        status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280",
                        commit_link = GenerateCommitLink(repositoryUrl, c.Sha)
                    }).ToList()
                }).ToList(),
                standalone_commits = ticketGroup.StandaloneCommits.Select(c => new
                {
                    short_sha = c.ShortSha,
                    sha = c.Sha,
                    message = c.Message,
                    author = c.Author,
                    date = c.Date.ToString("yyyy-MM-dd"),
                    status = ticketGroup.JiraInfo?.Status ?? "Unknown",
                    status_color = ticketGroup.JiraInfo != null ? GetStatusColor(ticketGroup.JiraInfo.Status) : "#6b7280",
                    commit_link = GenerateCommitLink(repositoryUrl, c.Sha)
                }).ToList(),
                dependencies = uniqueDependencies
                    .Where(dep => ticketLookup.ContainsKey(dep))
                    .Select(dep => new
                    {
                        ticket_number = dep,
                        status = ticketLookup[dep].JiraInfo?.Status ?? "Unknown",
                        status_color = ticketLookup[dep].JiraInfo != null ? GetStatusColor(ticketLookup[dep].JiraInfo?.Status) : "#6b7280"
                    }).ToList(),
                dependents = uniqueDependents
                    .Where(dep => ticketLookup.ContainsKey(dep) && !uniqueDependencies.Contains(dep))
                    .Select(dep => new
                    {
                        ticket_number = dep,
                        status = ticketLookup[dep].JiraInfo?.Status ?? "Unknown",
                        status_color = ticketLookup[dep].JiraInfo != null ? GetStatusColor(ticketLookup[dep].JiraInfo?.Status) : "#6b7280"
                    }).ToList()
            };
            
            result.Add(ticketData);
        }
        
        return result;
    }
    
    private static Dictionary<string, List<string>> AnalyzeTicketDependencies(List<TicketGroup> ticketGroups)
    {
        var dependencies = new Dictionary<string, List<string>>();
        var shaToTickets = new Dictionary<string, List<string>>();

        // Build SHA-to-ticket map
        foreach (var group in ticketGroups)
        {
            foreach (var mr in group.MergeRequests)
            {
                if (!shaToTickets.ContainsKey(mr.MergeCommit.Sha))
                    shaToTickets[mr.MergeCommit.Sha] = [];
                shaToTickets[mr.MergeCommit.Sha].Add(group.TicketNumber);
                foreach (var c in mr.MrCommits)
                {
                    if (!shaToTickets.ContainsKey(c.Sha))
                        shaToTickets[c.Sha] = [];
                    shaToTickets[c.Sha].Add(group.TicketNumber);
                }
            }
            foreach (var c in group.StandaloneCommits)
            {
                if (!shaToTickets.ContainsKey(c.Sha))
                    shaToTickets[c.Sha] = [];
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
            if (ticketDeps.Count != 0)
                dependencies[group.TicketNumber] = [.. ticketDeps];
        }
        return dependencies;
    }

    private static List<string> ExtractTicketReferences(string message)
    {
        var tickets = new List<string>();
        var words = message.Split([' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var word in words)
        {
            // Look for ticket patterns like HSAMED-1234, JIRA-567, etc.
            if (TicketKeyRegex().IsMatch(word))
            {
                tickets.Add(word);
            }
        }
        
        return tickets;
    }
    
    private static List<object> GetAvailableStatuses(List<TicketGroup> ticketGroups)
    {
        var statuses = ticketGroups
            .Where(tg => tg.TicketNumber != "No Ticket" && tg.JiraInfo != null)
            .GroupBy(tg => tg.JiraInfo!.Status)
            .Select(g => new
            {
                name = NormalizeStatusForDataAttribute(g.Key),
                display_name = g.Key,
                icon = GetStatusIcon(g.Key),
                count = g.Count()
            })
            .OrderBy(s => GetStatusPriority(s.display_name))
            .ToList<object>();
            
        // Add "No Ticket" if exists
        var noTicketGroup = ticketGroups.FirstOrDefault(tg => tg.TicketNumber == "No Ticket");
        if (noTicketGroup != null)
        {
            statuses.Add(new
            {
                name = "no-ticket",
                display_name = "No Ticket",
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
    
    private static string GetStatusColor(string? status)
    {
        status ??= string.Empty;
        return status.ToLower() switch
        {
            "to do" => "#3b82f6",
            "in progress" => "#f59e0b",
            "pending prod deployment" => "#ef4444",
            "prod deployed" => "#10b981",
            "done" => "#10b981",
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
    
    private static string NormalizeStatusForDataAttribute(string status)
    {
        if (string.IsNullOrEmpty(status) || status == "No Ticket")
            return "no-ticket";
        
        // Trim whitespace, convert to lowercase, and replace spaces with hyphens for consistency
        return status.Trim().ToLower().Replace(" ", "-");
    }
    
    private static string? GenerateCommitLink(string? repositoryUrl, string commitSha)
    {
        if (string.IsNullOrEmpty(repositoryUrl) || string.IsNullOrEmpty(commitSha))
            return null;
            
        // Handle different repository URL formats
        if (repositoryUrl.Contains("github.com"))
        {
            // GitHub: https://github.com/owner/repo/commit/{sha}
            return $"{repositoryUrl.TrimEnd('/')}/commit/{commitSha}";
        }
        else if (repositoryUrl.Contains("gitlab.com") || repositoryUrl.Contains("gitlab."))
        {
            // GitLab: https://gitlab.com/owner/repo/-/commit/{sha}
            return $"{repositoryUrl.TrimEnd('/')}/-/commit/{commitSha}";
        }
        else if (repositoryUrl.Contains("dev.azure.com") || repositoryUrl.Contains("visualstudio.com"))
        {
            // Azure DevOps: https://dev.azure.com/org/project/_git/repo/commit/{sha}
            return $"{repositoryUrl.TrimEnd('/')}/commit/{commitSha}";
        }
        else if (repositoryUrl.Contains("bitbucket.org"))
        {
            // Bitbucket: https://bitbucket.org/owner/repo/commits/{sha}
            return $"{repositoryUrl.TrimEnd('/')}/commits/{commitSha}";
        }
        
        // Generic fallback
        return $"{repositoryUrl.TrimEnd('/')}/commit/{commitSha}";
    }
    
    private static string? GenerateJiraLink(string? jiraBaseUrl, string ticketKey)
    {
        if (string.IsNullOrEmpty(jiraBaseUrl) || string.IsNullOrEmpty(ticketKey))
            return null;
            
        return $"{jiraBaseUrl.TrimEnd('/')}/browse/{ticketKey}";
    }

    [GeneratedRegex(@"^[A-Za-z]+-\d+$")]
    private static partial Regex TicketKeyRegex();
} 