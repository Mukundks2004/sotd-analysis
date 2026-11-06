using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sotd.Backend.Api;
using Sotd.Backend.Dtos;
using Sotd.Database;

namespace Sotd.Backend.Controllers;

// One controller per non-join table. Everything except the column list and the DTO mapping comes from
// CrudController. The "Small"/"Big" and default-ordering choices come straight from Database/spec.md.

[Route("api/people")]
public class PeopleController : CrudController<Person, PersonDto, PersonWrite>
{
    public PeopleController(SotdDbContext db) : base(db) { }
    protected override DbSet<Person> Set => Db.People;
    protected override Expression<Func<Person, Guid>> IdSelector => p => p.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => false;
    protected override IReadOnlyList<ColumnMap<Person>> Columns => new[]
    {
        Query.Col<Person, Guid>("id", p => p.Id),
        Query.Text<Person>("name", p => p.Name),
    };
    protected override PersonDto ToRead(Person p) => new(p.Id, p.Name);
    protected override Person NewEntity(PersonWrite w) => Apply(w, new Person { Id = Guid.NewGuid() });
    protected override void ApplyWrite(PersonWrite w, Person p) => Apply(w, p);
    static Person Apply(PersonWrite w, Person p) { p.Name = w.Name.Trim(); return p; }
}

[Route("api/profiles")]
public class ProfilesController : CrudController<Profile, ProfileDto, ProfileWrite>
{
    public ProfilesController(SotdDbContext db) : base(db) { }
    protected override DbSet<Profile> Set => Db.Profiles;
    protected override Expression<Func<Profile, Guid>> IdSelector => p => p.Id;
    protected override string DefaultColumn => "username";
    protected override bool IsBig => false;
    // PicturePath is sortable but deliberately not searched: the filter bar must ignore the picture column.
    protected override IReadOnlyList<ColumnMap<Profile>> Columns => new[]
    {
        Query.Col<Profile, Guid>("id", p => p.Id),
        Query.Col<Profile, long>("discordId", p => p.DiscordId),
        Query.Text<Profile>("username", p => p.Username),
        Query.Col<Profile, bool>("isCurrentMember", p => p.IsCurrentMember),
        Query.Col<Profile, string>("picturePath", p => p.PicturePath),
    };
    protected override ProfileDto ToRead(Profile p) =>
        new(p.Id, p.DiscordId.ToString(), p.Username, p.IsCurrentMember, p.PicturePath);
    protected override Profile NewEntity(ProfileWrite w) => Apply(w, new Profile { Id = Guid.NewGuid() });
    protected override void ApplyWrite(ProfileWrite w, Profile p) => Apply(w, p);
    static Profile Apply(ProfileWrite w, Profile p)
    {
        p.DiscordId = Snowflake.Parse(w.DiscordId, "Discord ID");
        p.Username = w.Username.Trim();
        p.IsCurrentMember = w.IsCurrentMember;
        p.PicturePath = w.PicturePath?.Trim() ?? "";
        return p;
    }
}

[Route("api/channels")]
public class ChannelsController : CrudController<Channel, ChannelDto, ChannelWrite>
{
    public ChannelsController(SotdDbContext db) : base(db) { }
    protected override DbSet<Channel> Set => Db.Channels;
    protected override Expression<Func<Channel, Guid>> IdSelector => c => c.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => false;
    protected override IReadOnlyList<ColumnMap<Channel>> Columns => new[]
    {
        Query.Col<Channel, Guid>("id", c => c.Id),
        Query.Col<Channel, long>("discordId", c => c.DiscordId),
        Query.Text<Channel>("name", c => c.Name),
    };
    protected override ChannelDto ToRead(Channel c) => new(c.Id, c.DiscordId.ToString(), c.Name);
    protected override Channel NewEntity(ChannelWrite w) => Apply(w, new Channel { Id = Guid.NewGuid() });
    protected override void ApplyWrite(ChannelWrite w, Channel c) => Apply(w, c);
    static Channel Apply(ChannelWrite w, Channel c)
    {
        c.DiscordId = Snowflake.Parse(w.DiscordId, "Discord ID");
        c.Name = w.Name.Trim();
        return c;
    }
}

[Route("api/messages")]
public class MessagesController : CrudController<Message, MessageDto, MessageWrite>
{
    public MessagesController(SotdDbContext db) : base(db) { }
    protected override DbSet<Message> Set => Db.Messages;
    protected override Expression<Func<Message, Guid>> IdSelector => m => m.Id;
    protected override string DefaultColumn => "postedAt";
    protected override bool DefaultDesc => true;
    protected override bool IsBig => true;
    protected override IReadOnlyList<ColumnMap<Message>> Columns => new[]
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
    protected override MessageDto ToRead(Message m) =>
        new(m.Id, m.DiscordId.ToString(), m.ChannelId, m.PostedById, m.PostedAt, m.EditedAt, m.Content, m.ReplyId);
    protected override Message NewEntity(MessageWrite w) => Apply(w, new Message { Id = Guid.NewGuid() });
    protected override void ApplyWrite(MessageWrite w, Message m) => Apply(w, m);
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

[Route("api/file-extensions")]
public class FileExtensionsController : CrudController<FileExtension, FileExtensionDto, FileExtensionWrite>
{
    public FileExtensionsController(SotdDbContext db) : base(db) { }
    protected override DbSet<FileExtension> Set => Db.FileExtensions;
    protected override Expression<Func<FileExtension, Guid>> IdSelector => f => f.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => false;
    protected override IReadOnlyList<ColumnMap<FileExtension>> Columns => new[]
    {
        Query.Col<FileExtension, Guid>("id", f => f.Id),
        Query.Text<FileExtension>("name", f => f.Name),
    };
    protected override FileExtensionDto ToRead(FileExtension f) => new(f.Id, f.Name);
    protected override FileExtension NewEntity(FileExtensionWrite w) => Apply(w, new FileExtension { Id = Guid.NewGuid() });
    protected override void ApplyWrite(FileExtensionWrite w, FileExtension f) => Apply(w, f);
    static FileExtension Apply(FileExtensionWrite w, FileExtension f) { f.Name = w.Name.Trim(); return f; }
}

[Route("api/attachments")]
public class AttachmentsController : CrudController<Attachment, AttachmentDto, AttachmentWrite>
{
    public AttachmentsController(SotdDbContext db) : base(db) { }
    protected override DbSet<Attachment> Set => Db.Attachments;
    protected override Expression<Func<Attachment, Guid>> IdSelector => a => a.Id;
    protected override string DefaultColumn => "filename";
    protected override bool IsBig => true;
    protected override IReadOnlyList<ColumnMap<Attachment>> Columns => new[]
    {
        Query.Col<Attachment, Guid>("id", a => a.Id),
        Query.Col<Attachment, long>("discordId", a => a.DiscordId),
        Query.Col<Attachment, Guid>("messageId", a => a.MessageId),
        Query.Text<Attachment>("filename", a => a.Filename),
        Query.Col<Attachment, Guid>("fileExtensionId", a => a.FileExtensionId),
        Query.Col<Attachment, long>("size", a => a.Size),
        Query.Text<Attachment>("path", a => a.Path),
    };
    protected override AttachmentDto ToRead(Attachment a) =>
        new(a.Id, a.DiscordId.ToString(), a.MessageId, a.Filename, a.FileExtensionId, a.Size, a.Path);
    protected override Attachment NewEntity(AttachmentWrite w) => Apply(w, new Attachment { Id = Guid.NewGuid() });
    protected override void ApplyWrite(AttachmentWrite w, Attachment a) => Apply(w, a);
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

[Route("api/emojis")]
public class EmojisController : CrudController<Emoji, EmojiDto, EmojiWrite>
{
    public EmojisController(SotdDbContext db) : base(db) { }
    protected override DbSet<Emoji> Set => Db.Emojis;
    protected override Expression<Func<Emoji, Guid>> IdSelector => e => e.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => false;
    protected override IReadOnlyList<ColumnMap<Emoji>> Columns => new[]
    {
        Query.Col<Emoji, Guid>("id", e => e.Id),
        Query.Text<Emoji>("name", e => e.Name),
        Query.Col<Emoji, long?>("discordId", e => e.DiscordId),
        Query.Col<Emoji, bool>("isAnimated", e => e.IsAnimated),
        Query.Text<Emoji>("path", e => e.Path),
    };
    protected override EmojiDto ToRead(Emoji e) => new(e.Id, e.Name, e.DiscordId?.ToString(), e.IsAnimated, e.Path);
    protected override Emoji NewEntity(EmojiWrite w) => Apply(w, new Emoji { Id = Guid.NewGuid() });
    protected override void ApplyWrite(EmojiWrite w, Emoji e) => Apply(w, e);
    static Emoji Apply(EmojiWrite w, Emoji e)
    {
        e.Name = w.Name.Trim();
        e.DiscordId = Snowflake.ParseOptional(w.DiscordId, "Discord ID");
        e.IsAnimated = w.IsAnimated;
        e.Path = string.IsNullOrWhiteSpace(w.Path) ? null : w.Path.Trim();
        return e;
    }
}

[Route("api/songs")]
public class SongsController : CrudController<Song, SongDto, SongWrite>
{
    public SongsController(SotdDbContext db) : base(db) { }
    protected override DbSet<Song> Set => Db.Songs;
    protected override Expression<Func<Song, Guid>> IdSelector => s => s.Id;
    protected override string DefaultColumn => "title";
    protected override bool IsBig => true;
    protected override IReadOnlyList<ColumnMap<Song>> Columns => new[]
    {
        Query.Col<Song, Guid>("id", s => s.Id),
        Query.Text<Song>("title", s => s.Title),
        Query.Text<Song>("spotifyId", s => s.SpotifyId),
        Query.Text<Song>("isrc", s => s.Isrc),
    };
    protected override SongDto ToRead(Song s) => new(s.Id, s.Title, s.SpotifyId, s.Isrc);
    protected override Song NewEntity(SongWrite w) => Apply(w, new Song { Id = Guid.NewGuid() });
    protected override void ApplyWrite(SongWrite w, Song s) => Apply(w, s);
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

[Route("api/genres")]
public class GenresController : CrudController<Genre, GenreDto, GenreWrite>
{
    public GenresController(SotdDbContext db) : base(db) { }
    protected override DbSet<Genre> Set => Db.Genres;
    protected override Expression<Func<Genre, Guid>> IdSelector => g => g.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => true;
    protected override IReadOnlyList<ColumnMap<Genre>> Columns => new[]
    {
        Query.Col<Genre, Guid>("id", g => g.Id),
        Query.Text<Genre>("name", g => g.Name),
        Query.Col<Genre, Guid?>("parentId", g => g.ParentId),
    };
    protected override GenreDto ToRead(Genre g) => new(g.Id, g.Name, g.ParentId);
    protected override Genre NewEntity(GenreWrite w) => Apply(w, new Genre { Id = Guid.NewGuid() });
    protected override void ApplyWrite(GenreWrite w, Genre g) => Apply(w, g);
    static Genre Apply(GenreWrite w, Genre g)
    {
        g.Name = w.Name.Trim();
        g.ParentId = w.ParentId;
        return g;
    }
}

[Route("api/artists")]
public class ArtistsController : CrudController<Artist, ArtistDto, ArtistWrite>
{
    public ArtistsController(SotdDbContext db) : base(db) { }
    protected override DbSet<Artist> Set => Db.Artists;
    protected override Expression<Func<Artist, Guid>> IdSelector => a => a.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => true;
    protected override IReadOnlyList<ColumnMap<Artist>> Columns => new[]
    {
        Query.Col<Artist, Guid>("id", a => a.Id),
        Query.Text<Artist>("name", a => a.Name),
        Query.Text<Artist>("spotifyId", a => a.SpotifyId),
    };
    protected override ArtistDto ToRead(Artist a) => new(a.Id, a.Name, a.SpotifyId);
    protected override Artist NewEntity(ArtistWrite w) => Apply(w, new Artist { Id = Guid.NewGuid() });
    protected override void ApplyWrite(ArtistWrite w, Artist a) => Apply(w, a);
    static Artist Apply(ArtistWrite w, Artist a)
    {
        a.Name = w.Name.Trim();
        a.SpotifyId = string.IsNullOrWhiteSpace(w.SpotifyId) ? null : w.SpotifyId.Trim();
        return a;
    }
}

[Route("api/sources")]
public class SourcesController : CrudController<Source, SourceDto, SourceWrite>
{
    public SourcesController(SotdDbContext db) : base(db) { }
    protected override DbSet<Source> Set => Db.Sources;
    protected override Expression<Func<Source, Guid>> IdSelector => s => s.Id;
    protected override string DefaultColumn => "name";
    protected override bool IsBig => false;
    protected override IReadOnlyList<ColumnMap<Source>> Columns => new[]
    {
        Query.Col<Source, Guid>("id", s => s.Id),
        Query.Text<Source>("name", s => s.Name),
    };
    protected override SourceDto ToRead(Source s) => new(s.Id, s.Name);
    protected override Source NewEntity(SourceWrite w) => Apply(w, new Source { Id = Guid.NewGuid() });
    protected override void ApplyWrite(SourceWrite w, Source s) => Apply(w, s);
    static Source Apply(SourceWrite w, Source s) { s.Name = w.Name.Trim(); return s; }
}

[Route("api/sotd-entries")]
public class SotdEntriesController : CrudController<SotdEntry, SotdEntryDto, SotdEntryWrite>
{
    public SotdEntriesController(SotdDbContext db) : base(db) { }
    protected override DbSet<SotdEntry> Set => Db.SotdEntries;
    protected override Expression<Func<SotdEntry, Guid>> IdSelector => e => e.Id;
    // The spec orders entries by the time of the message they belong to, newest first.
    protected override string DefaultColumn => "postedAt";
    protected override bool DefaultDesc => true;
    protected override bool IsBig => true;
    protected override IReadOnlyList<ColumnMap<SotdEntry>> Columns => new[]
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
    protected override SotdEntryDto ToRead(SotdEntry e) =>
        new(e.Id, e.SongId, e.MessageId, e.SourceId, e.Link, e.AttachmentId, e.Duration, e.ReleaseDate,
            e.Message.PostedAt);
    // The entry is ordered by the time of its message, so that time has to come back with the row for the
    // table to be able to show which column it is sorted on.
    protected override IQueryable<SotdEntry> Include(IQueryable<SotdEntry> source) => source.Include(e => e.Message);
    protected override SotdEntry NewEntity(SotdEntryWrite w) => Apply(w, new SotdEntry { Id = Guid.NewGuid() });
    protected override void ApplyWrite(SotdEntryWrite w, SotdEntry e) => Apply(w, e);
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
