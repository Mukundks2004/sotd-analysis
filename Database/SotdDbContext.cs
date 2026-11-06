using Microsoft.EntityFrameworkCore;

namespace Sotd.Database;

public class SotdDbContext : DbContext
{
    public SotdDbContext(DbContextOptions<SotdDbContext> options) : base(options) { }

    /// <summary>Builds a context for a sqlite file at <paramref name="path"/>.</summary>
    public static SotdDbContext ForSqliteFile(string path)
    {
        var options = new DbContextOptionsBuilder<SotdDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new SotdDbContext(options);
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<PersonProfile> PersonProfiles => Set<PersonProfile>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<FileExtension> FileExtensions => Set<FileExtension>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Emoji> Emojis => Set<Emoji>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Song> Songs => Set<Song>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<SongGenre> SongGenres => Set<SongGenre>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<SongArtist> SongArtists => Set<SongArtist>();
    public DbSet<ArtistGenre> ArtistGenres => Set<ArtistGenre>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SotdEntry> SotdEntries => Set<SotdEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All datetimes are UTC. SQLite has no zone info, so force Kind=Utc on the way back out.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // No cascade deletes anywhere: a delete that violates a constraint should fail.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        b.Entity<Person>().HasKey(x => x.Id);

        b.Entity<Profile>(e =>
        {
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.Username);
        });

        b.Entity<PersonProfile>(e =>
        {
            e.HasKey(x => new { x.PersonId, x.ProfileId });
        });

        b.Entity<Channel>(e =>
        {
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.Name);
        });

        b.Entity<Message>(e =>
        {
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.PostedAt);
            e.HasOne(x => x.Reply).WithMany().HasForeignKey(x => x.ReplyId);
        });

        b.Entity<FileExtension>(e => e.HasIndex(x => x.Name).IsUnique());

        b.Entity<Attachment>(e =>
        {
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.Filename);
        });

        b.Entity<Emoji>(e =>
        {
            // Unique when set; nulls (default emoji) may repeat.
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.Name);
            // Default (unicode) emoji have no Discord ID; their name (the character) is unique among them.
            e.HasIndex(x => x.Name).IsUnique().HasFilter("\"DiscordId\" IS NULL").HasDatabaseName("IX_Emojis_Name_Default");
        });

        b.Entity<Reaction>(e => e.HasKey(x => new { x.MessageId, x.ProfileId, x.EmojiId }));

        b.Entity<Song>(e =>
        {
            e.HasIndex(x => x.SpotifyId).IsUnique();
            e.HasIndex(x => x.Title);
        });

        b.Entity<Genre>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId);
        });

        b.Entity<SongGenre>(e => e.HasKey(x => new { x.SongId, x.GenreId }));

        b.Entity<Artist>(e =>
        {
            e.HasIndex(x => x.SpotifyId).IsUnique();
            e.HasIndex(x => x.Name);
        });

        b.Entity<SongArtist>(e =>
        {
            e.HasKey(x => new { x.SongId, x.ArtistId });
            e.Property(x => x.Position).HasDefaultValue(1);
        });

        b.Entity<ArtistGenre>(e => e.HasKey(x => new { x.ArtistId, x.GenreId }));

        b.Entity<Source>(e => e.HasIndex(x => x.Name).IsUnique());

        b.Entity<SotdEntry>(e =>
        {
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => x.SongId);
        });
    }
}

internal class UtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
}

internal class NullableUtcDateTimeConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        v => v == null ? null : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
        v => v == null ? null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) { }
}

/// <summary>Used only by `dotnet ef` at design time.</summary>
internal class DesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<SotdDbContext>
{
    public SotdDbContext CreateDbContext(string[] args) => SotdDbContext.ForSqliteFile("design-time.db");
}
