# Importer

We need to write a software that will import every "song of the day" from one or more discord channels into a local sqlite db. The software is written in C# (.NET 8), uses Discord.Net to read the channels, and uses EF Core (via the `Database` class library) to write to the db. The schema for this db is located in `Database/spec.md`. Read the spec first.

## Configuration

All input comes from a config file (e.g. `config.json`, gitignored, with a committed `config.example.json`). There are no CLI arguments. It contains:

- Database connection string (path to the sqlite file)
- Discord bot token
- Discord guild (server) ID
- List of Discord channel IDs to import
- Spotify client ID and client secret
- Spotify market (ISO country code, default `US`) — every Spotify call is pinned to one market so that track relinking cannot return a different ID for the same recording between runs
- Path to the `yt-dlp` executable (defaults to `yt-dlp` on the PATH)

The `Download/` folder is also gitignored.

## Flow

On start:

- create three folders in `Download/` in this repo if they do not exist: `Profiles`, `Attachments` and `Emojis`
- fetch the guild member list so that `Is Current Member` can be set for every profile

For each configured channel:

- if the channel is not in the `Channel` table, insert it
- go through the messages in the channel from oldest to most recent. If the channel already has messages in the db, start after the newest stored message instead of from the beginning. New reactions and edits on already-imported messages are not picked up; that is fine.
- skip system messages (joins, pins, etc.). Bot and webhook messages are imported.
- for each message, insert it into the db. If the message has already been written to the db (check by discord message ID snowflake) skip it
- otherwise insert it:
  - If the posting discord profile is not in the database (check by discord profile ID snowflake) enter it into the DB
    - Download the posting profile photo to `Download/Profiles/{snowflake}-{username}.{ext}`
  - `Reply` is the db message that this message replies to. If that message is not in the db (deleted, or in another channel) it is null.
  - If there are attachments, download them to `Download/Attachments/{snowflake}-{filename}` and insert the attachment row into the DB
    - If the extension for the file does not exist in the `File Extension` table, insert it into the file extension table first
  - If there are reactions, insert them into the reaction table. Fetching the reactors is slow; that is fine.
    - If the reacting profile is not in the database (check by discord profile ID snowflake) enter it into the DB
      - Download the reacting profile photo to `Download/Profiles/{snowflake}-{username}.{ext}`
    - If the emoji is a custom emoji and is not in the database (check by emoji ID snowflake) enter it into the DB
      - Download the emoji to `Download/Emojis/{snowflake}-{name}.{ext}`
    - If the emoji is a default (unicode) emoji and is not in the database (check by character) enter it with a null Discord ID and null path
  - Create SOTD entries (see below)

File names built from usernames, filenames or emoji names are sanitized of characters that are invalid in file names, and the real file extension is appended where the pattern above shows `{ext}`.

### Profile updates

Every run, for every profile already in the db that is seen (as a poster, a reactor, or in the guild member list), update its username, `Is Current Member` and profile picture. Profiles in the db but missing from the guild member list get `Is Current Member = false`.

## What counts as an entry

A message produces zero or more SOTD entries:

- every link in the message whose host is a known music or video host (YouTube, YouTube Music, Spotify, Apple Music, Deezer, SoundCloud, Bandcamp, Vimeo, Tidal, Amazon Music, etc.) is an entry. Each link in a message is its own entry.
- a YouTube, Spotify or Deezer playlist link produces one entry per track in the playlist. Non-song items in a Spotify playlist (podcast episodes, local files) are skipped.
- a link from a known URL shortener (`spotify.link`, `apple.co`, `bit.ly`, `t.co`, `lnk.to`, etc.) is followed to its destination and then classified normally. If it lands on a host we do not import, it is treated as "not an entry" rather than as a failure.
- every attached file that is an audio or video file is an entry (source `file upload`, no link, attachment set).
- **Not entries** links to any other host (tweets, images, gifs, news, etc.), image attachments, and any other media without audio. These links are written to a review log (see Logs).

Each distinct link is resolved at most once per run. URLs are cached under a key that ignores tracking parameters (`si`, `t`, `utm_*`, `feature`, ...) and query ordering, so the same track posted twice always resolves to the same result.

## Creating an entry

- First, work out the song title, artists, duration, release date and ISRC, in this order of preference:
  1. For a Spotify link, use the Spotify API directly.
  2. For an Apple Music or Deezer link, ask that service's own public API (iTunes Lookup / the Deezer API). These answer exactly for their own links, so nothing has to be guessed out of a page title. Deezer also returns the ISRC, which is then used to look the same recording up on Spotify exactly.
  3. For any other source, get the raw title/uploader/duration/upload date from the source (YouTube via `yt-dlp`, which also expands playlists; other sites via oEmbed/Open Graph/JSON-LD where available; file uploads by reading the file's metadata and duration). Then try to find the proper song and artists by searching the Spotify API with a string query.
  4. As a last resort, use the raw title of the source and the uploader name (youtube uploader) as the artist.
- Raw titles are cleaned before they are used or searched: bracketed and unbracketed noise (`Official Video`, `| Official Music Video`, `【MV】`, `(prod. by ...)`, `[HD]`, ...) is removed, `feat.`/`ft.`/`w/` credits are pulled out into additional artists, and uploader names are stripped of channel decoration (`VEVO`, `- Topic`, `Official Channel`). Uploader names that are clearly a channel, label or placeholder (`Various Artists`, `... Records`, ...) are not used as artists.
- A Spotify search considers every result and keeps the best-scoring one rather than the first plausible one. The score weights title (0.45), artist (0.30) and duration (0.25). A result is rejected outright when the title or, where artists are known, the artist does not match at all, or when its length is nothing like the posted media — that is what stops a three minute studio track being matched to a ten minute live upload. If nothing scores well enough, the entry falls back to the cleaned raw title and is written to the review log.
- JSON-LD on a page is only read as a song when it describes a recording. An album page is used only when the link named a specific track on it (e.g. Apple Music's `?i=`), so an album name is never stored as a song title.
- The duration is always the duration of the linked media (youtube, spotify, or the file). If no duration can be found, store `0` and write the entry to the review log.
- Match the song and artists to existing rows before inserting:
  - Artist: by Spotify ID first. If there is no Spotify ID, by exact name (case-sensitive).
  - Song: by ISRC first, then by Spotify ID, then by normalized title plus the ordered set of artists. The ISRC is the only identifier every service agrees on, so it is what collapses the same recording posted from Spotify, Apple Music and Deezer into one row.
  - Title normalization ignores reissue markers (`Remastered 2011`, `Radio Edit`, `Single Version`, `feat. ...`, ...) but deliberately keeps live, remix, acoustic, cover and instrumental versions distinct, because those are genuinely different recordings.
  - When a song is matched by name and the new entry carries a Spotify ID or ISRC the existing row lacks, the row adopts it (unless another row already holds it).
  - Otherwise insert new `Song` and `Artist` rows (and `Song-Artist` rows with the credited position).
- For a newly inserted artist, if they have a Spotify ID, give them all of their Spotify genres (insert any missing genres into `Genre` with no parent, matched by unique name). Do not give any genres to the song. The Song-Genre join stays empty.
- If the 'source' of the song does not exist in the source table, e.g., YouTube, spotify, apple music, amazon music, tiktok, YouTube shorts, etc., insert it.
- A link from a known host that cannot be resolved at all (deleted/private video, etc.) is skipped and written to the review log.

## Tables populated

Essentially, the importer starts with an empty DB and populates these tables:

- channel
- profile
- message
- emoji
- reaction
- song
- artist
- song-artist join
- genre
- artist-genre join
- song of the day entry
- source
- attachment
- file extension

It does not touch `Person`, `Person-Profile join` or `Song-Genre join`.

## Logs

The importer writes review logs (plain text, in a gitignored `Logs/` folder) for:

- skipped links (links on non-music hosts, with message ID)
- unresolvable links
- entries stored with duration `0`
- songs and artists matched by name/search only, or whose Spotify search was a guess, rather than by a confirmed Spotify ID
