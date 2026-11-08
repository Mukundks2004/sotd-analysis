namespace Importer;

/// <summary>Plain-text review logs under Logs/ (gitignored) for things the user should check by hand.</summary>
public class ReviewLog : IReviewLog
{
    readonly string _dir;
    readonly string _stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    readonly object _lock = new();

    public ReviewLog(string repoRoot)
    {
        _dir = Path.Combine(repoRoot, "Logs");
        Directory.CreateDirectory(_dir);
    }

    public void SkippedLink(string context, string url) => Write("skipped-links", $"{context}\t{url}");
    public void Unresolvable(string context, string url, string reason) => Write("unresolvable", $"{context}\t{url}\t{reason}");
    public void ZeroDuration(string context, string description) => Write("zero-duration", $"{context}\t{description}");
    public void NameMatched(string context, string description) => Write("name-matched", $"{context}\t{description}");

    void Write(string kind, string line)
    {
        lock (_lock)
            File.AppendAllText(Path.Combine(_dir, $"{_stamp}-{kind}.log"), line + Environment.NewLine);
    }
}
