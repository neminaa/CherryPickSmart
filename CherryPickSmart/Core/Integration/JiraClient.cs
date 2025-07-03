using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CherryPickSmart.Models.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CherryPickSmart.Core.Integration;

public class JiraClient
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationSettings _settings;
    private readonly ILogger<JiraClient> _logger;
    private readonly Dictionary<string, JiraTicket> _cache = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public JiraClient(HttpClient httpClient, IOptions<ApplicationSettings> options, ILogger<JiraClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        
        // Configure base address if not already set
        if (_httpClient.BaseAddress == null && !string.IsNullOrEmpty(_settings.Jira?.Url))
        {
            _httpClient.BaseAddress = new Uri(_settings.Jira.Url);
        }
        
        // Configure JSON options for case-insensitive deserialization
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public record JiraTicket
    {
        public string Key { get; init; } = "";
        public string Summary { get; init; } = "";
        public string Status { get; init; } = "";
        public string? Assignee { get; init; }
        public string Priority { get; init; } = "";
        public List<string> Labels { get; init; } = [];
    }

    public async Task<JiraTicket?> GetTicketAsync(string ticketKey)
    {
        if (_cache.TryGetValue(ticketKey, out var cached))
            return cached;

        if (string.IsNullOrEmpty(_settings.Jira?.Url))
        {
            _logger.LogWarning("Jira URL is not configured.");
            return null;
        }

        var url = $"/rest/api/2/issue/{ticketKey}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(_settings.Jira.Username) && !string.IsNullOrEmpty(_settings.Jira.ApiToken))
        {
            var authToken = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_settings.Jira.Username}:{_settings.Jira.ApiToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        }

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Failed to fetch ticket {ticketKey}. Status code: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var jiraResponse = JsonSerializer.Deserialize<JiraApiResponse>(json, _jsonOptions);

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
                Labels = jiraResponse.Fields.Labels ?? []
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

        if (uncachedKeys.Count == 0)
            return results;

        if (string.IsNullOrEmpty(_settings.Jira?.Url))
        {
            _logger.LogWarning("Jira URL is not configured.");
            return results;
        }

        foreach (var batch in uncachedKeys.Chunk(50))
        {
            var jql = $"key in ({string.Join(",", batch)})";
            var url = $"/rest/api/2/search?jql={Uri.EscapeDataString(jql)}&fields=summary,status,assignee,priority,labels";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(_settings.Jira.Username) && !string.IsNullOrEmpty(_settings.Jira.ApiToken))
            {
                var authToken = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_settings.Jira.Username}:{_settings.Jira.ApiToken}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            }

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to fetch batch tickets. Status code: {response.StatusCode}");
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var jiraResponse = JsonSerializer.Deserialize<JiraApiSearchResponse>(json, _jsonOptions);

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
                        Labels = issue.Fields.Labels ?? []
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
        [JsonPropertyName("key")]
        public string Key { get; init; } = "";
        
        [JsonPropertyName("fields")]
        public JiraFields Fields { get; init; } = new();
    }

    private record JiraApiSearchResponse
    {
        [JsonPropertyName("issues")]
        public List<JiraApiResponse> Issues { get; init; } = [];
    }

    private record JiraFields
    {
        [JsonPropertyName("summary")]
        public string Summary { get; init; } = "";
        
        [JsonPropertyName("status")]
        public JiraStatus Status { get; init; } = new();
        
        [JsonPropertyName("assignee")]
        public JiraAssignee? Assignee { get; init; }
        
        [JsonPropertyName("priority")]
        public JiraPriority? Priority { get; init; }
        
        [JsonPropertyName("labels")]
        public List<string>? Labels { get; init; }
    }

    private record JiraStatus
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
    }

    private record JiraAssignee
    {
        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = "";
    }

    private record JiraPriority
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";
    }
}

class TestJiraClient
{
    // Removed unused test Main method to fix entry point warning
}
