using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sotd.Importer;

public record YtDlpItem(
    string? Url,
    string? Title,
    string? Uploader,
    string? Track,
    List<string> Artists,
    double? Duration,
    DateTime? Date);

/// <summary>Runs yt-dlp to get metadata for a video/track link or expand a playlist. Never downloads media.</summary>
public class YtDlpClient
{
    readonly string _path;

    public YtDlpClient(string path) => _path = path;

    /// <summary>Throws if yt-dlp cannot be started. Call once at startup.</summary>
    public async Task EnsureAvailableAsync()
    {
        try
        {
            var (code, output, error) = await RunAsync(new[] { "--version" }, TimeSpan.FromSeconds(30));
            if (code != 0) throw new InvalidOperationException($"yt-dlp --version failed: {error}");
            Console.WriteLine($"yt-dlp {output.Trim()}");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not run yt-dlp at '{_path}'. Install it (e.g. `winget install yt-dlp`) or set YtDlpPath in config.json.", ex);
        }
    }

    /// <summary>
    /// Returns one item for a single video, or one per entry for a playlist/album.
    /// Returns an empty list if yt-dlp could not read the link (unsupported site, private video, ...).
    /// </summary>
    public async Task<List<YtDlpItem>> GetAsync(string url)
    {
        var (code, output, _) = await RunAsync(
            new[] { "-J", "--flat-playlist", "--no-playlist", "--no-warnings", url }, TimeSpan.FromMinutes(5));
        if (code != 0 || string.IsNullOrWhiteSpace(output)) return new();

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        var items = new List<YtDlpItem>();
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in entries.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object) items.Add(ParseItem(e));
        }
        else
        {
            items.Add(ParseItem(root));
        }
        return items;
    }

    static YtDlpItem ParseItem(JsonElement e)
    {
        string? Str(string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        double? duration = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetDouble() : null;

        var artists = new List<string>();
        if (e.TryGetProperty("artists", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
                if (a.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(a.GetString()))
                    artists.Add(a.GetString()!);
        if (artists.Count == 0 && Str("artist") is { Length: > 0 } single)
            artists.AddRange(Text.SplitArtistNames(single));
        if (artists.Count == 0 && Str("creator") is { Length: > 0 } creator)
            artists.AddRange(Text.SplitArtistNames(creator));

        var uploader = Str("uploader") ?? Str("channel");
        var date = ParseDate(Str("release_date")) ?? ParseDate(Str("upload_date"));
        return new YtDlpItem(Str("webpage_url") ?? Str("url"), Str("title"), uploader, Str("track"), artists, duration, date);
    }

    static DateTime? ParseDate(string? s) =>
        s != null && DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;

    async Task<(int Code, string Output, string Error)> RunAsync(IEnumerable<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(_path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start yt-dlp");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return (-1, "", "timed out");
        }
        return (proc.ExitCode, await stdout, await stderr);
    }
}
