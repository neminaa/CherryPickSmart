using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CherryPickSmart.Core.Integration;
using CherryPickSmart.Services;
using Microsoft.Extensions.Logging;

namespace CherryPickSmart.Core.Integration;

public class JiraClient
{
    private readonly HttpClient _httpClient;
    private readonly ConfigurationService _config;
    private readonly ILogger<JiraClient> _logger;
    private readonly Dictionary<string, JiraTicket> _cache = new();

    public JiraClient(HttpClient httpClient, ConfigurationService config, ILogger<JiraClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public record JiraTicket
    {
        public string Key { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Assignee { get; init; }
        public string Priority { get; init; } = "";
        public List<string> Labels { get; init; } = new();
    }

    public async Task<JiraTicket?> GetTicketAsync(string ticketKey)
    {
        if (_cache.TryGetValue(ticketKey, out var cached))
            return cached;

        var config = await _config.LoadConfigAsync();
        if (string.IsNullOrEmpty(config.JiraUrl))
        {
            _logger.LogWarning("Jira URL is not configured.");
            return null;
        }

        var url = $"{config.JiraUrl}/rest/api/2/issue/{ticketKey}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var authToken = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{config.JiraUsername}:{config.JiraApiToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to fetch ticket {ticketKey}. Status code: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var jiraResponse = JsonSerializer.Deserialize<JiraApiResponse>(json);

            if (jiraResponse == null)
            {
                _logger.LogWarning($"Failed to deserialize Jira response for ticket {ticketKey}.");
                return null;
            }

            var ticket = new JiraTicket
            {
                Key = jiraResponse.Key,
                Summary = jiraResponse.Fields.Summary,
                Status = jiraResponse.Fields.Status.Name,
                Assignee = jiraResponse.Fields.Assignee?.DisplayName,
                Priority = jiraResponse.Fields.Priority?.Name ?? "Medium",
                Labels = jiraResponse.Fields.Labels ?? new()
            };

            _cache[ticketKey] = ticket;
            return ticket;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error fetching ticket {ticketKey}: {ex.Message}");
            return null;
        }
    }

    public async Task<Dictionary<string, JiraTicket>> GetTicketsBatchAsync(List<string> ticketKeys)
    {
        var results = new Dictionary<string, JiraTicket>();

        var uncachedKeys = ticketKeys.Where(k => !_cache.ContainsKey(k)).ToList();
        foreach (var key in ticketKeys.Where(k => _cache.ContainsKey(k)))
        {
            results[key] = _cache[key];
        }

        if (!uncachedKeys.Any())
            return results;

        var config = await _config.LoadConfigAsync();
        if (string.IsNullOrEmpty(config.JiraUrl))
        {
            _logger.LogWarning("Jira URL is not configured.");
            return results;
        }

        foreach (var batch in uncachedKeys.Chunk(50))
        {
            var jql = $"key in ({string.Join(",", batch)})";
            var url = $"{config.JiraUrl}/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields=summary,status,assignee,priority,labels";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var authToken = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{config.JiraUsername}:{config.JiraApiToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to fetch batch tickets. Status code: {response.StatusCode}");
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var jiraResponse = JsonSerializer.Deserialize<JiraApiSearchResponse>(json);

                if (jiraResponse?.Issues == null)
                {
                    _logger.LogWarning("Failed to deserialize Jira batch response.");
                    continue;
                }

                foreach (var issue in jiraResponse.Issues)
                {
                    var ticket = new JiraTicket
                    {
                        Key = issue.Key,
                        Summary = issue.Fields.Summary,
                        Status = issue.Fields.Status.Name,
                        Assignee = issue.Fields.Assignee?.DisplayName,
                        Priority = issue.Fields.Priority?.Name ?? "Medium",
                        Labels = issue.Fields.Labels ?? new()
                    };

                    results[ticket.Key] = ticket;
                    _cache[ticket.Key] = ticket;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching batch tickets: {ex.Message}");
                continue;
            }
        }

        return results;
    }

    private record JiraApiResponse
    {
        public string Key { get; init; } = "";
        public JiraFields Fields { get; init; } = new();
    }

    private record JiraApiSearchResponse
    {
        public List<JiraApiResponse> Issues { get; init; } = new();
    }

    private record JiraFields
    {
        public string Summary { get; init; } = "";
        public JiraStatus Status { get; init; } = new();
        public JiraAssignee? Assignee { get; init; }
        public JiraPriority? Priority { get; init; }
        public List<string>? Labels { get; init; }
    }

    private record JiraStatus
    {
        public string Name { get; init; } = "";
    }

    private record JiraAssignee
    {
        public string DisplayName { get; init; } = "";
    }

    private record JiraPriority
    {
        public string Name { get; init; } = "";
    }
}

class TestJiraClient
{
    // Removed unused test Main method to fix entry point warning
}
