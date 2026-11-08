using Microsoft.EntityFrameworkCore;
using Importer;

// Exactly one flag picks the half to run:
//   --import   copy profiles, channels, messages, attachments, emojis and reactions out of Discord
//   --process  turn the imported messages into songs, links and SOTD entries (Spotify, Apple Music, yt-dlp, ...)
const string Usage = "Usage: Importer --import | --process";
var flags = args.Distinct().ToList();
ImporterMode mode;
if (flags is ["--import"]) mode = ImporterMode.Import;
else if (flags is ["--process"]) mode = ImporterMode.Process;
else
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    var repoRoot = RepoPaths.FindRepoRoot();
    var config = ImporterConfig.Load(RepoPaths.FindConfig(repoRoot), mode);

    var dbPath = Path.IsPathRooted(config.DatabasePath) ? config.DatabasePath : Path.Combine(repoRoot, config.DatabasePath);
    var dbs = new SotdDbContextFactory(dbPath);
    using (var db = dbs.Create())
        await db.Database.MigrateAsync();

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) sotd-analysis-importer");

    if (mode == ImporterMode.Import)
    {
        foreach (var folder in new[] { "Profiles", "Attachments", "Emojis" })
            Directory.CreateDirectory(Path.Combine(repoRoot, "Download", folder));

        var downloader = new FileDownloader(http);
        var importer = new DiscordImporter(config, repoRoot, dbs, downloader,
            new ProfileSync(downloader, repoRoot), new EmojiSync(downloader, repoRoot));
        await importer.RunAsync();
    }
    else
    {
        var ytdlp = new YtDlpClient(config.YtDlpPath);
        await ytdlp.EnsureAvailableAsync(); // fail now rather than silently losing every YouTube entry

        var log = new ReviewLog(repoRoot);
        var spotify = new SpotifyClient(http, config.SpotifyClientId, config.SpotifyClientSecret, config.SpotifyMarket,
            minInterval: TimeSpan.FromMilliseconds(config.SpotifyMinIntervalMs));
        var resolver = new MediaResolver(spotify, ytdlp, new PageMetadataClient(http),
            new ITunesClient(http, config.SpotifyMarket), new DeezerClient(http), new LinkClassifier(),
            new FileMetadataReader(), http, log);

        var songIndex = new SongTitleIndex();
        var processor = new MessageProcessor(config, repoRoot, dbs, resolver, songIndex,
            new CatalogWriterFactory(songIndex, spotify, log));
        await processor.RunAsync();
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Import stopped: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}
