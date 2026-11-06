namespace Sotd.Backend.Dtos;

// Read DTOs are flat: one property per real column, so the UI table and the CSV export can be built straight
// from them. Discord snowflakes go across as strings because they do not survive a JavaScript number.

public record PersonDto(Guid Id, string Name);
public record PersonWrite(string Name);

public record ProfileDto(Guid Id, string DiscordId, string Username, bool IsCurrentMember, string PicturePath);
public record ProfileWrite(string DiscordId, string Username, bool IsCurrentMember, string PicturePath);

public record ChannelDto(Guid Id, string DiscordId, string Name);
public record ChannelWrite(string DiscordId, string Name);

public record MessageDto(Guid Id, string DiscordId, Guid ChannelId, Guid PostedById, DateTime PostedAt,
    DateTime? EditedAt, string Content, Guid? ReplyId);
public record MessageWrite(string DiscordId, Guid ChannelId, Guid PostedById, DateTime PostedAt,
    DateTime? EditedAt, string Content, Guid? ReplyId);

public record FileExtensionDto(Guid Id, string Name);
public record FileExtensionWrite(string Name);

public record AttachmentDto(Guid Id, string DiscordId, Guid MessageId, string Filename, Guid FileExtensionId,
    long Size, string Path);
public record AttachmentWrite(string DiscordId, Guid MessageId, string Filename, Guid FileExtensionId,
    long Size, string Path);

public record EmojiDto(Guid Id, string Name, string? DiscordId, bool IsAnimated, string? Path);
public record EmojiWrite(string Name, string? DiscordId, bool IsAnimated, string? Path);

public record SongDto(Guid Id, string Title, string? SpotifyId, string? Isrc);
public record SongWrite(string Title, string? SpotifyId, string? Isrc);

public record GenreDto(Guid Id, string Name, Guid? ParentId);
public record GenreWrite(string Name, Guid? ParentId);

public record ArtistDto(Guid Id, string Name, string? SpotifyId);
public record ArtistWrite(string Name, string? SpotifyId);

public record SourceDto(Guid Id, string Name);
public record SourceWrite(string Name);

public record SotdEntryDto(Guid Id, Guid SongId, Guid MessageId, Guid SourceId, string? Link, Guid? AttachmentId,
    int Duration, DateTime? ReleaseDate);
public record SotdEntryWrite(Guid SongId, Guid MessageId, Guid SourceId, string? Link, Guid? AttachmentId,
    int Duration, DateTime? ReleaseDate);

/// <summary>Turns the snowflake strings the UI sends back into the long the database stores.</summary>
public static class Snowflake
{
    public static long Parse(string? value, string column) =>
        long.TryParse(value?.Trim(), out var id)
            ? id
            : throw new BadHttpRequestException($"{column} must be a Discord ID (a whole number).");

    public static long? ParseOptional(string? value, string column) =>
        string.IsNullOrWhiteSpace(value) ? null : Parse(value, column);
}
