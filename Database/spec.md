# Database

This is a human readable schema of the database. All columns are not nullable unless otherwise specified.

The database is SQLite, accessed through EF Core. The schema in a C# class library in this folder (entities, `DbContext` and migrations), which the Importer (and later the Backend) reference. `schema.sql` is also kept here.

For each table I've noted if there are a small, finite number of rows (e.g., there are only so many people around 10 that have contributed to SOTD) or a growing number of rows (e.g., `Message` table grows every day)

There should be no on delete cascade- if deletes fail due to a constraint, that is good.

## Conventions

- Every `ID` is a UUID (stored as text).
- Every "Discord ID" is a Discord snowflake, stored as a 64-bit `INTEGER`.
- All datetimes are in UTC.
- Columns marked "unique" have a unique constraint. Every Discord ID column is unique within its table (except nullable ones, where nulls are allowed to repeat).
- Files are saved to disk under `Download/` and the relevant row stores the path to the file (relative to the repo root).

## Person [Small] Order By Name Alphabetical

> This table is for a real life person who has contributed entries to SOTD. The importer does not populate this table.

- ID (UUID)
- Name (string)

## Profile [Small] Order By Username Alphabetical

> This table is for a discord profile

- ID (UUID)
- Discord user ID (snowflake, unique)
- Username (this is at the time of running the script, since usernames can change. We don't care about username history) (string)
- Is Current Member (Whether or not this profile is in the server at the time of running the script) (boolean)
- Path to profile picture file (we are going to save all media, and keep the path to each one in the relevant entity) (string)

## Join Entity Between Person and Profile

> A person can have multiple profiles, and a profile can be shared by multiple people, so there needs to be a many to many. The importer does not populate this table.

- Person (Foreign key reference to `Person`)
- Profile (Foreign key reference to `Profile`)

## Channel [Small] Order By Name Alphabetical

> A discord channel that messages are imported from

- ID (UUID)
- Discord channel ID (snowflake, unique)
- Name (string)

## Message [Big] Order By Time, Newest First

> A discord message- it is important to track messages because the spirit of SOTD is in channel and all the supporting/reaction messages to songs are imporant too. Not every message is a SOTD but every SOTD is tied to a message. Bot and webhook messages are included. System messages (joins, pins, etc.) are not.

- ID (UUID)
- Discord ID (snowflake, unique)
- Channel (Foreign key reference to `Channel`)
- Posted by (Foreign key reference to `Profile`)
- Posted at (this is in UTC and should be specific to the millisecond if possible) (Datetime)
- Edited at (same as above) Datetime, nullable because most messages are never edited
- Content (this is the raw string content of the message. If a file is uploaded with no text this will be the empty string) (string)
- Reply (Foreign key reference to `Message` if this message is replying to another one, but nullable. Null if the replied-to message was deleted or is in another channel)

## File Extension [Small] Order By Name Alphabetical

> This is for normalisation for the `Attachment` table

- ID (UUID)
- Name (string, unique)

## Attachment [Big] Order By Filename Alphabetical

> Sometimes messages have attached files

- ID (UUID)
- Discord ID (snowflake, unique)
- Message (Foreign key reference to `Message`)
- Filename (this includes the file extension) (string)
- File Extension (Foreign key reference to `File Extension`)
- Size (in bytes) (int)
- Path to file (string)

## Emoji [Small] Order By Name Alphabetical

> For normalisation for the `Reaction` table. Includes both custom server emoji and default (unicode) emoji.

- ID (UUID)
- Name (string. For a default emoji this is the unicode character itself, e.g. 👍. Unique among default emoji)
- Discord ID (snowflake, unique) nullable- null for default emoji
- Is Animated (boolean. Always false for default emoji)
- Path to file (string) nullable- null for default emoji

## Reaction

> For provenance. Functionally a join entity between emoji and message, since an emoji can be used on many messages and a message can have many emojis, and even a message with a given emoji can have multiple reactions from different profiles.

- Message (Foreign key reference to `Message`)
- Profile (Foreign key reference to `Profile`)
- Emoji (Foreign key reference to `Emoji`)

The primary key is the composite (Message, Profile, Emoji).

## Song [Big] Order By Title Alphabetical

> A song is an entity because sometimes the same song is posted multiple times

- ID (UUID)
- Title (string) (not unique, multiple songs with same name)
- Spotify ID (string, unique) nullable- the Spotify track ID, used to match the same song across posts and sources

## Genre [Big] Order By Name Alphabetical

> Every artist has 0 or many genres (the importer never gives genres to songs, but the join exists). A genre is the type of song, e.g., pop, rock, classical. Genres can form a tree like structure- some genres have parents. For example, k-pop is a child of pop. The importer inserts genres with no parent; parents are filled in later by hand.

- ID (UUID)
- Name (string, unique)
- Parent (Foreign key reference to `Genre`, but nullable)

## Join Entity Between Song and Genre

> A song can have multiple genres, and genres can be on many songs. The importer leaves this table empty.

- Song (Foreign key reference to `Song`)
- Genre (Foreign key reference to `Genre`)

## Artist [Big] Order By Name Alphabetical

> An artist is a person who made or helped make the song

- ID (UUID)
- Name (not necessarily unique, artists can have the same stage name) (string)
- Spotify ID (string, unique) nullable- the Spotify artist ID, used to tell apart artists with the same name

## Join Entity Between Song and Artist

> A song can have multiple artists, and artists can make many songs

- Artist (Foreign key reference to `Artist`)
- Song (Foreign key reference to `Song`)
- Position (this is where the artist is credited- if they wrote it they are first, but they might be second or third or fourth) (int with default of `1` since this is mostly `1`)

## Join Entity Between Artist and Genre

> An artist can have multiple genres, and genres can be on many artists

- Artist (Foreign key reference to `Artist`)
- Genre (Foreign key reference to `Genre`)

## Source [Small] Order By Name Alphabetical

> This is for where a song can come from, since there are finitely many values. Examples are youtube, spotify, soundcloud, apple music, shazam, bandcamp, vimeo, file upload, other

- ID (UUID)
- Name (string, unique)

## SOTD Entry [Big] Order By Message 'Sent At' Time Newest First

> This is the main thing we are interested in- one song posted by a person on a day. We are not storing 'day' as a column because we can find that information through the message

- ID (UUID)
- Song (Foreign key reference to `Song`)
- Message (Foreign key reference to `Message`)
- Source (Foreign key reference to `Source`)
- Link (string) nullable because if the source type is file upload there is no link
- Attachment (Foreign key reference to `Attachment`) nullable because not every sotd is an attachment. Even though you can find the attachment by going to the message, if there are multiple attachments, you get the same song, same message, same source, but different attachments.
- Duration (seconds) this is on this table and not the song table because a song might have different durations depending on the source. This is the duration of the linked media (youtube, spotify, or the file). If the importer cannot find a duration it stores `0` and logs the entry for review.
- Release Date (datetime) nullable, same table placement as above for the same reason. Null when the source does not provide one.
