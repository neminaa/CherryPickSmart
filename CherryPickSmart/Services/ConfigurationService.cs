using System.Text.Json;

namespace CherryPickSmart.Services;

public class ConfigurationService
{
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cherry-pick-smart",
        "config.json");

    public record Config
    {
        public string? JiraUrl { get; init; }
        public string? JiraUsername { get; init; }
        public string? JiraApiToken { get; init; }
        public List<string> TicketPrefixes { get; init; } = ["HSAMED"];
        public string DefaultFromBranch { get; init; } = "deploy/dev";
        public string DefaultToBranch { get; init; } = "deploy/uat";
    }

    public async Task<Config> LoadConfigAsync()
    {
        if (!File.Exists(_configPath))
            return new Config();

        var json = await File.ReadAllTextAsync(_configPath);
        return JsonSerializer.Deserialize<Config>(json) ?? new Config();
    }

    public async Task SaveConfigAsync(Config config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, json);
    }
}
