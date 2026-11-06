# Database

This is a human readable schema of the database. All columns are not nullable unless otherwise specified.

For each table I've noted if there are a small, finite number of rows (e.g., there are only so many people around 10 that have contributed to SOTD) or a growing number of rows (e.g., `Message` table grows every day)

There should be no on delete cascade- if deletes fail due to a constraint, that is good.

TODO: Find the type of the discord ID system for profile ID, message ID, channel ID, etc.

## Person [Small] Order By Name Alphabetical

> This table is for a real life person who has contributed entries to SOTD

- ID (UUID)
- Name (string)

## Profile [Small] Order By Username Alphabetical

> This table is for a discord profile

- ID (UUID)
- Discord user ID (not sure of the type, this is discord's unique id for the profile)
- Username (this is at the time of running the script, since usernames can change. We don't care about username history) (string)
- Is Current Member (Whether or not this profile is in the server at the time of running the script) (boolean)
- Profile picture (not sure of the file type, but this should be a binary and should store data for the picture)

## Join Entity Between Person and Profile

> A person can have multiple profiles, and a profile can be shared by multiple people, so there needs to be a many to many

- Person (Foreign key reference to `Person`)
- Profile (Foreign key reference to `Profile`)

## Message [Big] Order By Time, Newest First

> A discord message- it is important to track messages because the spirit of SOTD is in channel and all the supporting/reaction messages to songs are imporant too. Not every message is a SOTD but every SOTD is tied to a message

- ID (UUID)
- Discord ID (again not sure of the type but discord should have some way to uniquely identify messages)
- Channel (which channel the message was posted in, same type as above)
- Posted by (Foreign key reference to `Profile`)
- Posted at (this is in UTC and should be specific to the second or millisecond if possible) (Datetime)
- Edited at (same as above) Datetime
- Content (this is the raw string content of the message, I think if a file is uploaded in the message there might not be any text so the content might be the empty string, but I am not sure) (string)
- Reply (Foreign key reference to `Message` if this message is replying to another one, but nullable)
- Raw Json (TODO I think this is because when we scrape the discord server, each message is stored as a json. If this is not the case, this column should be deleted) (string)

## File Extension [Small] Order By Name Alphabetical

> This is for normalisation for the `Attachment` table

- ID (UUID)
- Name (string, should be unique)

## Attachment [Big] Order By Filename Alphabetical

> Sometimes messages have attached files

- ID (UUID)
- Message (Foreign key reference to `Message`)
- Filename (this includes the file extension) (string)
- File Extension (Foreign key reference to `Extension`)
- Size (in bytes) (int)

## Emoji [Small] Order By Name Alphabetical

> For normalisation for the `Reaction` table

- ID (UUID)
- Name (string)
- Discord Id (again not sure of the type of this)
- Is Animated (boolean)

## Reaction

> For provenance. Functionally a join entity between emoji and message, since an emoji can be used on many messages and a message can have many emojis, and even a message with a given emoji can have multiple reactions from different profiles.

- Message (Foreign key reference to `Message`)
- Profile (Foreign key reference to `Profile`)
- Emoji (Foreign key reference to `Emoji`)

## Song [Big] Order By Title Alphabetical

> A song is an entity because sometimes the same song is posted multiple times

- ID (UUID)
- Title (string) (not unique, multiple songs with same name)

## Genre [Big] Order By Name Alphabetical

> Every song has 0 or many genres. A genre is the type of song, e.g., pop, rock, classical. Genres can form a tree like structure- some genres have parents. For example, k-pop is a child of pop

- ID (UUID)
- Name (string) should be unique
- Parent (Foreign key reference to `Genre`, but nullable)

## Join Entity Between Song and Genre

> A song can have multiple genres, and genres can be on many songs

- Artist (Foreign key reference to `Artist`)
- Song (Foreign key reference to `Song`)
- Position (this is where the artist is credited- if they wrote it they are first, but they might be second or third or fourth) (int with default of `1` since this is mostly `1`)

## Artist [Big] Order By Name Alphabetical

> An artist is a person who made or helped make the song

- ID (UUID)
- Name (not necessarily unique, artists can have the same stage name) (string)

## Join Entity Between Song and Artist

> A song can have multiple artists, and artists can make many songs

- Artist (Foreign key reference to `Artist`)
- Song (Foreign key reference to `Song`)
- Position (this is where the artist is credited- if they wrote it they are first, but they might be second or third or fourth) (int with default of `1` since this is mostly `1`)

## Source [Small] Order By Name Alphabetical

> This is for where a song can come from, since there are finitely many values. Examples are youtube, spotify, soundcloud, apple music, shazam, bandcamp, vimeo, file upload, other

- ID (UUID)
- Name (string) should be unique

## SOTD Entry [Big] Order By Message 'Sent At' Time Newest First

> This is the main thing we are interested in- one song posted by a person on a day. We are not storing 'day' as a column because we can find that information through the message

- ID (UUID)
- Song (Foreign key reference to `Song`)
- Message (Foreign key reference to `Message`)
- Source (Foreign key reference to `Source`)
- Link (string) nullable because if the source type is file upload there is no link
- Attachment (Foreign key reference to `Attachment`) nullable because not every sotd is an attachment. TODO Should this be removed? You can find attachment by going to the message and then seeing if there is an attachment that points to the message
- Duration (seconds) this is on this table and not the song table because a song might have different durations depending on the source.
