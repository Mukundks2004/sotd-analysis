using Microsoft.EntityFrameworkCore;
using Sotd.Database;
using Sotd.Importer;

// All input comes from config.json (see config.example.json). No command line arguments.
try
{
    var repoRoot = RepoPaths.FindRepoRoot();
    var config = ImporterConfig.Load(RepoPaths.FindConfig(repoRoot));

    foreach (var folder in new[] { "Profiles", "Attachments", "Emojis" })
        Directory.CreateDirectory(Path.Combine(repoRoot, "Download", folder));

    var dbPath = Path.IsPathRooted(config.DatabasePath) ? config.DatabasePath : Path.Combine(repoRoot, config.DatabasePath);
    var dbs = new SotdDbContextFactory(dbPath);
    using (var db = dbs.Create())
        await db.Database.MigrateAsync();

    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) sotd-analysis-importer");

    var ytdlp = new YtDlpClient(config.YtDlpPath);
    await ytdlp.EnsureAvailableAsync(); // fail now rather than silently losing every YouTube entry

    var log = new ReviewLog(repoRoot);
    var spotify = new SpotifyClient(http, config.SpotifyClientId, config.SpotifyClientSecret, config.SpotifyMarket,
        minInterval: TimeSpan.FromMilliseconds(config.SpotifyMinIntervalMs));
    var downloader = new FileDownloader(http);
    var resolver = new MediaResolver(spotify, ytdlp, new PageMetadataClient(http),
        new ITunesClient(http, config.SpotifyMarket), new DeezerClient(http), new LinkClassifier(),
        new FileMetadataReader(), http, log);

    var songIndex = new SongTitleIndex();
    var importer = new DiscordImporter(config, repoRoot, dbs, downloader, resolver,
        new ProfileSync(downloader, repoRoot), new EmojiSync(downloader, repoRoot), songIndex,
        new CatalogWriterFactory(songIndex, spotify, log));

    await importer.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Import stopped: {ex.Message}");
    Console.Error.WriteLine(ex);
    return 1;
}
