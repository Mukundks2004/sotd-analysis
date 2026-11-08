using Microsoft.EntityFrameworkCore.Design;

namespace Database;

internal class DesignTimeFactory : IDesignTimeDbContextFactory<SotdDbContext>
{
    public SotdDbContext CreateDbContext(string[] args) => SotdDbContext.ForSqliteFile("design-time.db");
}