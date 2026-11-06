namespace Sotd.Importer;

public record FileMetadata(string? Title, string? Artist, int DurationSeconds);

/// <summary>Reads duration and tags out of an uploaded audio/video file.</summary>
public static class FileMetadataReader
{
    public static FileMetadata? Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var artist = tag.FirstPerformer ?? tag.FirstAlbumArtist;
            return new FileMetadata(
                string.IsNullOrWhiteSpace(tag.Title) ? null : tag.Title,
                string.IsNullOrWhiteSpace(artist) ? null : artist,
                (int)Math.Round(file.Properties.Duration.TotalSeconds));
        }
        catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException or IOException)
        {
            return null;
        }
    }
}

public class FileDownloader
{
    readonly HttpClient _http;

    public FileDownloader(HttpClient http) => _http = http;

    /// <summary>Downloads to <paramref name="destination"/> (retrying a few times). Returns false if it never worked.</summary>
    public async Task<bool> TryDownloadAsync(string url, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var temp = destination + ".part";
            try
            {
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using (var src = await resp.Content.ReadAsStreamAsync())
                await using (var dst = File.Create(temp))
                    await src.CopyToAsync(dst);
                File.Move(temp, destination, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (attempt == 3) return false;
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
        return false;
    }

    /// <summary>File extension (with dot) taken from a URL's path, ignoring the query string.</summary>
    public static string ExtensionFromUrl(string url, string fallback = ".png")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return fallback;
        var ext = Path.GetExtension(uri.AbsolutePath);
        return string.IsNullOrEmpty(ext) ? fallback : ext.ToLowerInvariant();
    }
}
