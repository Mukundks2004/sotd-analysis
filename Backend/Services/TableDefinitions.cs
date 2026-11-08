using System.Linq.Expressions;
using Backend.Api;
using Backend.Dtos;
using Database;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

// One definition per non-join table. The "Small"/"Big" and default-ordering choices come straight from
// Database/spec.md. Everything else about a table's endpoints lives in CrudService.

/// <summary>Fills in the parts most tables leave alone.</summary>
public abstract class TableDefinition<TEntity, TRead, TWrite> : ITableDefinition<TEntity, TRead, TWrite>
    where TEntity : class
{
    public abstract DbSet<TEntity> Set(SotdDbContext db);
    public abstract IReadOnlyList<ColumnMap<TEntity>> Columns { get; }
    public abstract Expression<Func<TEntity, Guid>> IdSelector { get; }
    public abstract string DefaultColumn { get; }
    public virtual bool DefaultDesc => false;
    public abstract bool IsBig { get; }
    public abstract TRead ToRead(TEntity entity);
    public abstract TEntity NewEntity(TWrite write);
    public abstract void ApplyWrite(TWrite write, TEntity entity);
    public virtual IQueryable<TEntity> Include(IQueryable<TEntity> source) => source;
}

public class PersonTable : TableDefinition<Person, PersonDto, PersonWrite>
{
    public override DbSet<Person> Set(SotdDbContext db) => db.People;
    public override Expression<Func<Person, Guid>> IdSelector => p => p.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => false;
    public override IReadOnlyList<ColumnMap<Person>> Columns => new[]
    {
        Query.Col<Person, Guid>("id", p => p.Id),
        Query.Text<Person>("name", p => p.Name),
    };
    public override PersonDto ToRead(Person p) => new(p.Id, p.Name);
    public override Person NewEntity(PersonWrite w) => Apply(w, new Person { Id = Guid.NewGuid() });
    public override void ApplyWrite(PersonWrite w, Person p) => Apply(w, p);
    static Person Apply(PersonWrite w, Person p) { p.Name = w.Name.Trim(); return p; }
}

public class ProfileTable : TableDefinition<Profile, ProfileDto, ProfileWrite>
{
    public override DbSet<Profile> Set(SotdDbContext db) => db.Profiles;
    public override Expression<Func<Profile, Guid>> IdSelector => p => p.Id;
    public override string DefaultColumn => "username";
    public override bool IsBig => false;
    // PicturePath is sortable but deliberately not searched: the filter bar must ignore the picture column.
    public override IReadOnlyList<ColumnMap<Profile>> Columns => new[]
    {
        Query.Col<Profile, Guid>("id", p => p.Id),
        Query.Col<Profile, long>("discordId", p => p.DiscordId),
        Query.Text<Profile>("username", p => p.Username),
        Query.Col<Profile, bool>("isCurrentMember", p => p.IsCurrentMember),
        Query.Col<Profile, string>("picturePath", p => p.PicturePath),
    };
    public override ProfileDto ToRead(Profile p) =>
        new(p.Id, p.DiscordId.ToString(), p.Username, p.IsCurrentMember, p.PicturePath);
    public override Profile NewEntity(ProfileWrite w) => Apply(w, new Profile { Id = Guid.NewGuid() });
    public override void ApplyWrite(ProfileWrite w, Profile p) => Apply(w, p);
    static Profile Apply(ProfileWrite w, Profile p)
    {
        p.DiscordId = Snowflake.Parse(w.DiscordId, "Discord ID");
        p.Username = w.Username.Trim();
        p.IsCurrentMember = w.IsCurrentMember;
        p.PicturePath = w.PicturePath?.Trim() ?? "";
        return p;
    }
}

public class ChannelTable : TableDefinition<Channel, ChannelDto, ChannelWrite>
{
    public override DbSet<Channel> Set(SotdDbContext db) => db.Channels;
    public override Expression<Func<Channel, Guid>> IdSelector => c => c.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => false;
    public override IReadOnlyList<ColumnMap<Channel>> Columns => new[]
    {
        Query.Col<Channel, Guid>("id", c => c.Id),
        Query.Col<Channel, long>("discordId", c => c.DiscordId),
        Query.Text<Channel>("name", c => c.Name),
    };
    public override ChannelDto ToRead(Channel c) => new(c.Id, c.DiscordId.ToString(), c.Name);
    public override Channel NewEntity(ChannelWrite w) => Apply(w, new Channel { Id = Guid.NewGuid() });
    public override void ApplyWrite(ChannelWrite w, Channel c) => Apply(w, c);
    static Channel Apply(ChannelWrite w, Channel c)
    {
        c.DiscordId = Snowflake.Parse(w.DiscordId, "Discord ID");
        c.Name = w.Name.Trim();
        return c;
    }
}

public class MessageTable : TableDefinition<Message, MessageDto, MessageWrite>
{
    public override DbSet<Message> Set(SotdDbContext db) => db.Messages;
    public override Expression<Func<Message, Guid>> IdSelector => m => m.Id;
    public override string DefaultColumn => "postedAt";
    public override bool DefaultDesc => true;
    public override bool IsBig => true;
    public override IReadOnlyList<ColumnMap<Message>> Columns => new[]
    {
        Query.Col<Message, Guid>("id", m => m.Id),
        Query.Col<Message, long>("discordId", m => m.DiscordId),
        Query.Col<Message, Guid>("channelId", m => m.ChannelId),
        Query.Col<Message, Guid>("postedById", m => m.PostedById),
        Query.Col<Message, DateTime>("postedAt", m => m.PostedAt),
        Query.Col<Message, DateTime?>("editedAt", m => m.EditedAt),
        Query.Text<Message>("content", m => m.Content),
        Query.Col<Message, Guid?>("replyId", m => m.ReplyId),
    };
    public override MessageDto ToRead(Message m) =>
        new(m.Id, m.DiscordId.ToString(), m.ChannelId, m.PostedById, m.PostedAt, m.EditedAt, m.Content, m.ReplyId);
    public override Message NewEntity(MessageWrite w) => Apply(w, new Message { Id = Guid.NewGuid() });
    public override void ApplyWrite(MessageWrite w, Message m) => Apply(w, m);
    static Message Apply(MessageWrite w, Message m)
    {
        m.DiscordId = Snowflake.Parse(w.DiscordId, "Discord ID");
        m.ChannelId = w.ChannelId;
        m.PostedById = w.PostedById;
        m.PostedAt = DateTime.SpecifyKind(w.PostedAt, DateTimeKind.Utc);
        m.EditedAt = w.EditedAt is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null;
        m.Content = w.Content ?? "";
        m.ReplyId = w.ReplyId;
        return m;
    }
}

public class FileExtensionTable : TableDefinition<FileExtension, FileExtensionDto, FileExtensionWrite>
{
    public override DbSet<FileExtension> Set(SotdDbContext db) => db.FileExtensions;
    public override Expression<Func<FileExtension, Guid>> IdSelector => f => f.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => false;
    public override IReadOnlyList<ColumnMap<FileExtension>> Columns => new[]
    {
        Query.Col<FileExtension, Guid>("id", f => f.Id),
        Query.Text<FileExtension>("name", f => f.Name),
    };
    public override FileExtensionDto ToRead(FileExtension f) => new(f.Id, f.Name);
    public override FileExtension NewEntity(FileExtensionWrite w) => Apply(w, new FileExtension { Id = Guid.NewGuid() });
    public override void ApplyWrite(FileExtensionWrite w, FileExtension f) => Apply(w, f);
    static FileExtension Apply(FileExtensionWrite w, FileExtension f) { f.Name = w.Name.Trim(); return f; }
}

public class AttachmentTable : TableDefinition<Attachment, AttachmentDto, AttachmentWrite>
{
    public override DbSet<Attachment> Set(SotdDbContext db) => db.Attachments;
    public override Expression<Func<Attachment, Guid>> IdSelector => a => a.Id;
    public override string DefaultColumn => "filename";
    public override bool IsBig => true;
    public override IReadOnlyList<ColumnMap<Attachment>> Columns => new[]
    {
        Query.Col<Attachment, Guid>("id", a => a.Id),
        Query.Col<Attachment, long>("discordId", a => a.DiscordId),
        Query.Col<Attachment, Guid>("messageId", a => a.MessageId),
        Query.Text<Attachment>("filename", a => a.Filename),
        Query.Col<Attachment, Guid>("fileExtensionId", a => a.FileExtensionId),
        Query.Col<Attachment, long>("size", a => a.Size),
        Query.Text<Attachment>("path", a => a.Path),
    };
    public override AttachmentDto ToRead(Attachment a) =>
        new(a.Id, a.DiscordId.ToString(), a.MessageId, a.Filename, a.FileExtensionId, a.Size, a.Path);
    public override Attachment NewEntity(AttachmentWrite w) => Apply(w, new Attachment { Id = Guid.NewGuid() });
    public override void ApplyWrite(AttachmentWrite w, Attachment a) => Apply(w, a);
    static Attachment Apply(AttachmentWrite w, Attachment a)
    {
        a.DiscordId = Snowflake.Parse(w.DiscordId, "Discord ID");
        a.MessageId = w.MessageId;
        a.Filename = w.Filename.Trim();
        a.FileExtensionId = w.FileExtensionId;
        a.Size = w.Size;
        a.Path = w.Path?.Trim() ?? "";
        return a;
    }
}

public class EmojiTable : TableDefinition<Emoji, EmojiDto, EmojiWrite>
{
    public override DbSet<Emoji> Set(SotdDbContext db) => db.Emojis;
    public override Expression<Func<Emoji, Guid>> IdSelector => e => e.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => false;
    public override IReadOnlyList<ColumnMap<Emoji>> Columns => new[]
    {
        Query.Col<Emoji, Guid>("id", e => e.Id),
        Query.Text<Emoji>("name", e => e.Name),
        Query.Col<Emoji, long?>("discordId", e => e.DiscordId),
        Query.Col<Emoji, bool>("isAnimated", e => e.IsAnimated),
        Query.Text<Emoji>("path", e => e.Path),
    };
    public override EmojiDto ToRead(Emoji e) => new(e.Id, e.Name, e.DiscordId?.ToString(), e.IsAnimated, e.Path);
    public override Emoji NewEntity(EmojiWrite w) => Apply(w, new Emoji { Id = Guid.NewGuid() });
    public override void ApplyWrite(EmojiWrite w, Emoji e) => Apply(w, e);
    static Emoji Apply(EmojiWrite w, Emoji e)
    {
        e.Name = w.Name.Trim();
        e.DiscordId = Snowflake.ParseOptional(w.DiscordId, "Discord ID");
        e.IsAnimated = w.IsAnimated;
        e.Path = string.IsNullOrWhiteSpace(w.Path) ? null : w.Path.Trim();
        return e;
    }
}

public class SongTable : TableDefinition<Song, SongDto, SongWrite>
{
    public override DbSet<Song> Set(SotdDbContext db) => db.Songs;
    public override Expression<Func<Song, Guid>> IdSelector => s => s.Id;
    public override string DefaultColumn => "title";
    public override bool IsBig => true;
    public override IReadOnlyList<ColumnMap<Song>> Columns => new[]
    {
        Query.Col<Song, Guid>("id", s => s.Id),
        Query.Text<Song>("title", s => s.Title),
        Query.Text<Song>("spotifyId", s => s.SpotifyId),
        Query.Text<Song>("isrc", s => s.Isrc),
    };
    public override SongDto ToRead(Song s) => new(s.Id, s.Title, s.SpotifyId, s.Isrc);
    public override Song NewEntity(SongWrite w) => Apply(w, new Song { Id = Guid.NewGuid() });
    public override void ApplyWrite(SongWrite w, Song s) => Apply(w, s);
    static Song Apply(SongWrite w, Song s)
    {
        s.Title = w.Title.Trim();
        s.SpotifyId = Blank(w.SpotifyId);
        s.Isrc = Blank(w.Isrc);
        return s;
    }
    // A unique column has to be null when it is empty, otherwise two blank rows collide with each other.
    static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

public class GenreTable : TableDefinition<Genre, GenreDto, GenreWrite>
{
    public override DbSet<Genre> Set(SotdDbContext db) => db.Genres;
    public override Expression<Func<Genre, Guid>> IdSelector => g => g.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => true;
    public override IReadOnlyList<ColumnMap<Genre>> Columns => new[]
    {
        Query.Col<Genre, Guid>("id", g => g.Id),
        Query.Text<Genre>("name", g => g.Name),
        Query.Col<Genre, Guid?>("parentId", g => g.ParentId),
    };
    public override GenreDto ToRead(Genre g) => new(g.Id, g.Name, g.ParentId);
    public override Genre NewEntity(GenreWrite w) => Apply(w, new Genre { Id = Guid.NewGuid() });
    public override void ApplyWrite(GenreWrite w, Genre g) => Apply(w, g);
    static Genre Apply(GenreWrite w, Genre g)
    {
        g.Name = w.Name.Trim();
        g.ParentId = w.ParentId;
        return g;
    }
}

public class ArtistTable : TableDefinition<Artist, ArtistDto, ArtistWrite>
{
    public override DbSet<Artist> Set(SotdDbContext db) => db.Artists;
    public override Expression<Func<Artist, Guid>> IdSelector => a => a.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => true;
    public override IReadOnlyList<ColumnMap<Artist>> Columns => new[]
    {
        Query.Col<Artist, Guid>("id", a => a.Id),
        Query.Text<Artist>("name", a => a.Name),
        Query.Text<Artist>("spotifyId", a => a.SpotifyId),
    };
    public override ArtistDto ToRead(Artist a) => new(a.Id, a.Name, a.SpotifyId);
    public override Artist NewEntity(ArtistWrite w) => Apply(w, new Artist { Id = Guid.NewGuid() });
    public override void ApplyWrite(ArtistWrite w, Artist a) => Apply(w, a);
    static Artist Apply(ArtistWrite w, Artist a)
    {
        a.Name = w.Name.Trim();
        a.SpotifyId = string.IsNullOrWhiteSpace(w.SpotifyId) ? null : w.SpotifyId.Trim();
        return a;
    }
}

public class SourceTable : TableDefinition<Source, SourceDto, SourceWrite>
{
    public override DbSet<Source> Set(SotdDbContext db) => db.Sources;
    public override Expression<Func<Source, Guid>> IdSelector => s => s.Id;
    public override string DefaultColumn => "name";
    public override bool IsBig => false;
    public override IReadOnlyList<ColumnMap<Source>> Columns => new[]
    {
        Query.Col<Source, Guid>("id", s => s.Id),
        Query.Text<Source>("name", s => s.Name),
    };
    public override SourceDto ToRead(Source s) => new(s.Id, s.Name);
    public override Source NewEntity(SourceWrite w) => Apply(w, new Source { Id = Guid.NewGuid() });
    public override void ApplyWrite(SourceWrite w, Source s) => Apply(w, s);
    static Source Apply(SourceWrite w, Source s) { s.Name = w.Name.Trim(); return s; }
}

public class SotdEntryTable : TableDefinition<SotdEntry, SotdEntryDto, SotdEntryWrite>
{
    public override DbSet<SotdEntry> Set(SotdDbContext db) => db.SotdEntries;
    public override Expression<Func<SotdEntry, Guid>> IdSelector => e => e.Id;
    // The spec orders entries by the time of the message they belong to, newest first.
    public override string DefaultColumn => "postedAt";
    public override bool DefaultDesc => true;
    public override bool IsBig => true;
    public override IReadOnlyList<ColumnMap<SotdEntry>> Columns => new[]
    {
        Query.Col<SotdEntry, Guid>("id", e => e.Id),
        Query.Col<SotdEntry, Guid>("songId", e => e.SongId),
        Query.Col<SotdEntry, Guid>("messageId", e => e.MessageId),
        Query.Col<SotdEntry, Guid>("sourceId", e => e.SourceId),
        Query.Text<SotdEntry>("link", e => e.Link),
        Query.Col<SotdEntry, Guid?>("attachmentId", e => e.AttachmentId),
        Query.Col<SotdEntry, int>("duration", e => e.Duration),
        Query.Col<SotdEntry, DateTime?>("releaseDate", e => e.ReleaseDate),
        Query.Col<SotdEntry, DateTime>("postedAt", e => e.Message.PostedAt),
    };
    public override SotdEntryDto ToRead(SotdEntry e) =>
        new(e.Id, e.SongId, e.MessageId, e.SourceId, e.Link, e.AttachmentId, e.Duration, e.ReleaseDate,
            e.Message.PostedAt);
    // The entry is ordered by the time of its message, so that time has to come back with the row for the
    // table to be able to show which column it is sorted on.
    public override IQueryable<SotdEntry> Include(IQueryable<SotdEntry> source) => source.Include(e => e.Message);
    public override SotdEntry NewEntity(SotdEntryWrite w) => Apply(w, new SotdEntry { Id = Guid.NewGuid() });
    public override void ApplyWrite(SotdEntryWrite w, SotdEntry e) => Apply(w, e);
    static SotdEntry Apply(SotdEntryWrite w, SotdEntry e)
    {
        e.SongId = w.SongId;
        e.MessageId = w.MessageId;
        e.SourceId = w.SourceId;
        e.Link = string.IsNullOrWhiteSpace(w.Link) ? null : w.Link.Trim();
        e.AttachmentId = w.AttachmentId;
        e.Duration = w.Duration;
        e.ReleaseDate = w.ReleaseDate is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
        return e;
    }
}
