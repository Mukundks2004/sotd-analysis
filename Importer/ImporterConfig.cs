using System.Text.Json;

namespace Sotd.Importer;

public class ImporterConfig
{
    /// <summary>Path to the sqlite file. Relative paths are relative to the repo root.</summary>
    public string DatabasePath { get; set; } = "sotd.db";
    public string DiscordToken { get; set; } = "";
    public ulong GuildId { get; set; }
    public List<ulong> ChannelIds { get; set; } = new();
    public string SpotifyClientId { get; set; } = "";
    public string SpotifyClientSecret { get; set; } = "";
    /// <summary>
    /// Two letter market used for every Spotify and iTunes lookup. It has to be pinned: Spotify hands back a
    /// different track id for the same recording depending on the market, which otherwise stores one song twice.
    /// </summary>
    public string SpotifyMarket { get; set; } = "US";
    public string YtDlpPath { get; set; } = "yt-dlp";

    /// <summary>
    /// How many messages may have their network work (link resolution, yt-dlp, downloads, reaction users)
    /// in flight at once. The database is still written by a single thread in message order, so this only
    /// affects how fast the slow part goes. Keep it modest: every unit is a potential yt-dlp process and a
    /// share of the Spotify rate limit.
    /// </summary>
    public int MaxParallelism { get; set; } = 8;

    public static ImporterConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<ImporterConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) ?? throw new InvalidOperationException($"{path} is empty.");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(DiscordToken)) missing.Add(nameof(DiscordToken));
        if (GuildId == 0) missing.Add(nameof(GuildId));
        if (ChannelIds.Count == 0) missing.Add(nameof(ChannelIds));
        if (string.IsNullOrWhiteSpace(SpotifyClientId)) missing.Add(nameof(SpotifyClientId));
        if (string.IsNullOrWhiteSpace(SpotifyClientSecret)) missing.Add(nameof(SpotifyClientSecret));
        if (missing.Count > 0)
            throw new InvalidOperationException("Config is missing required values: " + string.Join(", ", missing));
        if (string.IsNullOrWhiteSpace(YtDlpPath)) YtDlpPath = "yt-dlp";
        if (string.IsNullOrWhiteSpace(DatabasePath)) DatabasePath = "sotd.db";
        MaxParallelism = Math.Clamp(MaxParallelism, 1, 32);
        SpotifyMarket = string.IsNullOrWhiteSpace(SpotifyMarket) ? "US" : SpotifyMarket.Trim().ToUpperInvariant();
    }
}

/// <summary>Finds the repo root (the folder holding SotdAnalysis.slnx) and the config file.</summary>
public static class RepoPaths
{
    public static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "SotdAnalysis.slnx")))
                    return dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    public static string FindConfig(string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "config.json"),
            Path.Combine(repoRoot, "Importer", "config.json"),
            Path.Combine(AppContext.BaseDirectory, "config.json"),
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                "config.json not found. Copy Importer/config.example.json to Importer/config.json and fill it in.");
    }
}
