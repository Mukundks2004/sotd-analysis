using Database;

namespace Importer;

/// <summary>Opens a context against the configured SQLite file. One per message, so a failed message rolls back on its own.</summary>
public class SotdDbContextFactory : ISotdDbContextFactory
{
    readonly string _dbPath;

    public SotdDbContextFactory(string dbPath) => _dbPath = dbPath;

    public SotdDbContext Create() => SotdDbContext.ForSqliteFile(_dbPath);
}
