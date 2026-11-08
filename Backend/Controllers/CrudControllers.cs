using Backend.Api;
using Backend.Dtos;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Sotd.Database;

namespace Backend.Controllers;

// One controller per non-join table. Each is just a route and a service: what makes the tables differ
// lives in the matching ITableDefinition, and the shared behaviour in CrudService.

[Route("api/people")]
public class PeopleController : CrudController<Person, PersonDto, PersonWrite>
{
    public PeopleController(ICrudService<Person, PersonDto, PersonWrite> service) : base(service) { }
}

[Route("api/profiles")]
public class ProfilesController : CrudController<Profile, ProfileDto, ProfileWrite>
{
    public ProfilesController(ICrudService<Profile, ProfileDto, ProfileWrite> service) : base(service) { }
}

[Route("api/channels")]
public class ChannelsController : CrudController<Channel, ChannelDto, ChannelWrite>
{
    public ChannelsController(ICrudService<Channel, ChannelDto, ChannelWrite> service) : base(service) { }
}

[Route("api/messages")]
public class MessagesController : CrudController<Message, MessageDto, MessageWrite>
{
    public MessagesController(ICrudService<Message, MessageDto, MessageWrite> service) : base(service) { }
}

[Route("api/file-extensions")]
public class FileExtensionsController : CrudController<FileExtension, FileExtensionDto, FileExtensionWrite>
{
    public FileExtensionsController(ICrudService<FileExtension, FileExtensionDto, FileExtensionWrite> service) : base(service) { }
}

[Route("api/attachments")]
public class AttachmentsController : CrudController<Attachment, AttachmentDto, AttachmentWrite>
{
    public AttachmentsController(ICrudService<Attachment, AttachmentDto, AttachmentWrite> service) : base(service) { }
}

[Route("api/emojis")]
public class EmojisController : CrudController<Emoji, EmojiDto, EmojiWrite>
{
    public EmojisController(ICrudService<Emoji, EmojiDto, EmojiWrite> service) : base(service) { }
}

[Route("api/songs")]
public class SongsController : CrudController<Song, SongDto, SongWrite>
{
    public SongsController(ICrudService<Song, SongDto, SongWrite> service) : base(service) { }
}

[Route("api/genres")]
public class GenresController : CrudController<Genre, GenreDto, GenreWrite>
{
    public GenresController(ICrudService<Genre, GenreDto, GenreWrite> service) : base(service) { }
}

[Route("api/artists")]
public class ArtistsController : CrudController<Artist, ArtistDto, ArtistWrite>
{
    public ArtistsController(ICrudService<Artist, ArtistDto, ArtistWrite> service) : base(service) { }
}

[Route("api/sources")]
public class SourcesController : CrudController<Source, SourceDto, SourceWrite>
{
    public SourcesController(ICrudService<Source, SourceDto, SourceWrite> service) : base(service) { }
}

[Route("api/sotd-entries")]
public class SotdEntriesController : CrudController<SotdEntry, SotdEntryDto, SotdEntryWrite>
{
    public SotdEntriesController(ICrudService<SotdEntry, SotdEntryDto, SotdEntryWrite> service) : base(service) { }
}
