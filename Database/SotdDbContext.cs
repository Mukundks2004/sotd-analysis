using Database.Converters;
using Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Database;

public class SotdDbContext(DbContextOptions<SotdDbContext> options) : DbContext(options)
{
    public static SotdDbContext ForSqliteFile(string path)
    {
        var options = new DbContextOptionsBuilder<SotdDbContext>().UseSqlite($"Data Source={path}").Options;
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
    public DbSet<LinkSource> LinkSources => Set<LinkSource>();
    public DbSet<SongLink> SongLinks => Set<SongLink>();
    public DbSet<AttachmentSong> AttachmentSongs => Set<AttachmentSong>();
    public DbSet<SotdEntry> SotdEntries => Set<SotdEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
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
            e.HasOne(x => x.Reply).WithMany(x => x.Replies).HasForeignKey(x => x.ReplyId);
        });

        b.Entity<FileExtension>(e => e.HasIndex(x => x.Name).IsUnique());

        b.Entity<Attachment>(e =>
        {
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.Filename);
        });

        b.Entity<Emoji>(e =>
        {
            e.HasIndex(x => x.DiscordId).IsUnique();
            e.HasIndex(x => x.Name).IsUnique().HasFilter("\"DiscordId\" IS NULL").HasDatabaseName("IX_Emojis_Name_Default");
        });

        b.Entity<Reaction>(e => e.HasKey(x => new { x.MessageId, x.ProfileId, x.EmojiId }));

        b.Entity<Song>(e =>
        {
            e.HasIndex(x => x.SpotifyId).IsUnique();
            e.HasIndex(x => x.AppleId).IsUnique();
            e.HasIndex(x => x.YoutubeId).IsUnique();
            e.HasIndex(x => x.Isrc).IsUnique();
            e.HasIndex(x => x.Title);
        });

        b.Entity<Genre>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId);
        });

        b.Entity<SongGenre>(e => e.HasKey(x => new { x.SongId, x.GenreId }));

        b.Entity<Artist>(e =>
        {
            e.HasIndex(x => x.SpotifyId).IsUnique();
            e.HasIndex(x => x.AppleId).IsUnique();
            e.HasIndex(x => x.YoutubeChannelId).IsUnique();
            e.HasIndex(x => x.Name);
        });

        b.Entity<SongArtist>(e =>
        {
            e.HasKey(x => new { x.SongId, x.ArtistId });
            e.Property(x => x.Position).HasDefaultValue(1);
        });

        b.Entity<ArtistGenre>(e => e.HasKey(x => new { x.ArtistId, x.GenreId }));

        b.Entity<LinkSource>(e => e.HasIndex(x => x.Name).IsUnique());

        b.Entity<SongLink>(e => e.HasIndex(x => x.Url).IsUnique());

        b.Entity<AttachmentSong>(e =>
        {
            e.HasKey(x => x.AttachmentId);
            e.HasOne(x => x.Attachment).WithOne(x => x.AttachmentSong).HasForeignKey<AttachmentSong>(x => x.AttachmentId);
        });

        b.Entity<SotdEntry>(e =>
        {
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => x.SongLinkId);
            e.HasIndex(x => x.AttachmentSongId);

            // SOTD Entry has song through AttachmentSong or through SongLink (but not both)
            e.ToTable(t => t.HasCheckConstraint(
                "CK_SotdEntries_ExactlyOneSource",
                "(\"SongLinkId\" IS NULL) <> (\"AttachmentSongId\" IS NULL)"));
        });

        // No cascade deletes
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
