CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;

CREATE TABLE "Artists" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Artists" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "SpotifyId" TEXT NULL,
    "AppleId" TEXT NULL,
    "YoutubeChannelId" TEXT NULL
);

CREATE TABLE "Channels" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Channels" PRIMARY KEY,
    "DiscordId" INTEGER NOT NULL,
    "Name" TEXT NOT NULL
);

CREATE TABLE "Emojis" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Emojis" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "DiscordId" INTEGER NULL,
    "IsAnimated" INTEGER NOT NULL,
    "Path" TEXT NULL
);

CREATE TABLE "FileExtensions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_FileExtensions" PRIMARY KEY,
    "Name" TEXT NOT NULL
);

CREATE TABLE "Genres" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Genres" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "ParentId" TEXT NULL,
    CONSTRAINT "FK_Genres_Genres_ParentId" FOREIGN KEY ("ParentId") REFERENCES "Genres" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "LinkSources" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_LinkSources" PRIMARY KEY,
    "Name" TEXT NOT NULL
);

CREATE TABLE "People" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_People" PRIMARY KEY,
    "Name" TEXT NOT NULL
);

CREATE TABLE "Profiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Profiles" PRIMARY KEY,
    "DiscordId" INTEGER NOT NULL,
    "Username" TEXT NOT NULL,
    "IsCurrentMember" INTEGER NOT NULL,
    "PicturePath" TEXT NOT NULL
);

CREATE TABLE "Songs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Songs" PRIMARY KEY,
    "Title" TEXT NOT NULL,
    "SpotifyId" TEXT NULL,
    "AppleId" TEXT NULL,
    "YoutubeId" TEXT NULL,
    "Isrc" TEXT NULL
);

CREATE TABLE "ArtistGenres" (
    "ArtistId" TEXT NOT NULL,
    "GenreId" TEXT NOT NULL,
    CONSTRAINT "PK_ArtistGenres" PRIMARY KEY ("ArtistId", "GenreId"),
    CONSTRAINT "FK_ArtistGenres_Artists_ArtistId" FOREIGN KEY ("ArtistId") REFERENCES "Artists" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_ArtistGenres_Genres_GenreId" FOREIGN KEY ("GenreId") REFERENCES "Genres" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Messages" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY,
    "DiscordId" INTEGER NOT NULL,
    "ChannelId" TEXT NOT NULL,
    "PostedById" TEXT NOT NULL,
    "PostedAt" TEXT NOT NULL,
    "EditedAt" TEXT NULL,
    "Content" TEXT NOT NULL,
    "ReplyId" TEXT NULL,
    CONSTRAINT "FK_Messages_Channels_ChannelId" FOREIGN KEY ("ChannelId") REFERENCES "Channels" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Messages_Messages_ReplyId" FOREIGN KEY ("ReplyId") REFERENCES "Messages" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Messages_Profiles_PostedById" FOREIGN KEY ("PostedById") REFERENCES "Profiles" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PersonProfiles" (
    "PersonId" TEXT NOT NULL,
    "ProfileId" TEXT NOT NULL,
    CONSTRAINT "PK_PersonProfiles" PRIMARY KEY ("PersonId", "ProfileId"),
    CONSTRAINT "FK_PersonProfiles_People_PersonId" FOREIGN KEY ("PersonId") REFERENCES "People" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_PersonProfiles_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "SongArtists" (
    "SongId" TEXT NOT NULL,
    "ArtistId" TEXT NOT NULL,
    "Position" INTEGER NOT NULL DEFAULT 1,
    CONSTRAINT "PK_SongArtists" PRIMARY KEY ("SongId", "ArtistId"),
    CONSTRAINT "FK_SongArtists_Artists_ArtistId" FOREIGN KEY ("ArtistId") REFERENCES "Artists" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SongArtists_Songs_SongId" FOREIGN KEY ("SongId") REFERENCES "Songs" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "SongGenres" (
    "SongId" TEXT NOT NULL,
    "GenreId" TEXT NOT NULL,
    CONSTRAINT "PK_SongGenres" PRIMARY KEY ("SongId", "GenreId"),
    CONSTRAINT "FK_SongGenres_Genres_GenreId" FOREIGN KEY ("GenreId") REFERENCES "Genres" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SongGenres_Songs_SongId" FOREIGN KEY ("SongId") REFERENCES "Songs" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "SongLinks" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SongLinks" PRIMARY KEY,
    "Url" TEXT NOT NULL,
    "SongId" TEXT NOT NULL,
    "LinkSourceId" TEXT NOT NULL,
    "Duration" INTEGER NOT NULL,
    "UploadDate" TEXT NULL,
    "Path" TEXT NULL,
    "Lyrics" TEXT NULL,
    CONSTRAINT "FK_SongLinks_LinkSources_LinkSourceId" FOREIGN KEY ("LinkSourceId") REFERENCES "LinkSources" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SongLinks_Songs_SongId" FOREIGN KEY ("SongId") REFERENCES "Songs" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Attachments" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Attachments" PRIMARY KEY,
    "DiscordId" INTEGER NOT NULL,
    "MessageId" TEXT NOT NULL,
    "Filename" TEXT NOT NULL,
    "FileExtensionId" TEXT NOT NULL,
    "Size" INTEGER NOT NULL,
    "Path" TEXT NOT NULL,
    CONSTRAINT "FK_Attachments_FileExtensions_FileExtensionId" FOREIGN KEY ("FileExtensionId") REFERENCES "FileExtensions" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Attachments_Messages_MessageId" FOREIGN KEY ("MessageId") REFERENCES "Messages" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Reactions" (
    "MessageId" TEXT NOT NULL,
    "ProfileId" TEXT NOT NULL,
    "EmojiId" TEXT NOT NULL,
    CONSTRAINT "PK_Reactions" PRIMARY KEY ("MessageId", "ProfileId", "EmojiId"),
    CONSTRAINT "FK_Reactions_Emojis_EmojiId" FOREIGN KEY ("EmojiId") REFERENCES "Emojis" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Reactions_Messages_MessageId" FOREIGN KEY ("MessageId") REFERENCES "Messages" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Reactions_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AttachmentSongs" (
    "AttachmentId" TEXT NOT NULL CONSTRAINT "PK_AttachmentSongs" PRIMARY KEY,
    "SongId" TEXT NOT NULL,
    CONSTRAINT "FK_AttachmentSongs_Attachments_AttachmentId" FOREIGN KEY ("AttachmentId") REFERENCES "Attachments" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AttachmentSongs_Songs_SongId" FOREIGN KEY ("SongId") REFERENCES "Songs" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "SotdEntries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SotdEntries" PRIMARY KEY,
    "MessageId" TEXT NOT NULL,
    "SongLinkId" TEXT NULL,
    "AttachmentSongId" TEXT NULL,
    CONSTRAINT "CK_SotdEntries_ExactlyOneSource" CHECK (("SongLinkId" IS NULL) <> ("AttachmentSongId" IS NULL)),
    CONSTRAINT "FK_SotdEntries_AttachmentSongs_AttachmentSongId" FOREIGN KEY ("AttachmentSongId") REFERENCES "AttachmentSongs" ("AttachmentId") ON DELETE RESTRICT,
    CONSTRAINT "FK_SotdEntries_Messages_MessageId" FOREIGN KEY ("MessageId") REFERENCES "Messages" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SotdEntries_SongLinks_SongLinkId" FOREIGN KEY ("SongLinkId") REFERENCES "SongLinks" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_ArtistGenres_GenreId" ON "ArtistGenres" ("GenreId");

CREATE UNIQUE INDEX "IX_Artists_AppleId" ON "Artists" ("AppleId");

CREATE INDEX "IX_Artists_Name" ON "Artists" ("Name");

CREATE UNIQUE INDEX "IX_Artists_SpotifyId" ON "Artists" ("SpotifyId");

CREATE UNIQUE INDEX "IX_Artists_YoutubeChannelId" ON "Artists" ("YoutubeChannelId");

CREATE UNIQUE INDEX "IX_Attachments_DiscordId" ON "Attachments" ("DiscordId");

CREATE INDEX "IX_Attachments_FileExtensionId" ON "Attachments" ("FileExtensionId");

CREATE INDEX "IX_Attachments_Filename" ON "Attachments" ("Filename");

CREATE INDEX "IX_Attachments_MessageId" ON "Attachments" ("MessageId");

CREATE INDEX "IX_AttachmentSongs_SongId" ON "AttachmentSongs" ("SongId");

CREATE UNIQUE INDEX "IX_Channels_DiscordId" ON "Channels" ("DiscordId");

CREATE INDEX "IX_Channels_Name" ON "Channels" ("Name");

CREATE UNIQUE INDEX "IX_Emojis_DiscordId" ON "Emojis" ("DiscordId");

CREATE UNIQUE INDEX "IX_Emojis_Name_Default" ON "Emojis" ("Name") WHERE "DiscordId" IS NULL;

CREATE UNIQUE INDEX "IX_FileExtensions_Name" ON "FileExtensions" ("Name");

CREATE UNIQUE INDEX "IX_Genres_Name" ON "Genres" ("Name");

CREATE INDEX "IX_Genres_ParentId" ON "Genres" ("ParentId");

CREATE UNIQUE INDEX "IX_LinkSources_Name" ON "LinkSources" ("Name");

CREATE INDEX "IX_Messages_ChannelId" ON "Messages" ("ChannelId");

CREATE UNIQUE INDEX "IX_Messages_DiscordId" ON "Messages" ("DiscordId");

CREATE INDEX "IX_Messages_PostedAt" ON "Messages" ("PostedAt");

CREATE INDEX "IX_Messages_PostedById" ON "Messages" ("PostedById");

CREATE INDEX "IX_Messages_ReplyId" ON "Messages" ("ReplyId");

CREATE INDEX "IX_PersonProfiles_ProfileId" ON "PersonProfiles" ("ProfileId");

CREATE UNIQUE INDEX "IX_Profiles_DiscordId" ON "Profiles" ("DiscordId");

CREATE INDEX "IX_Profiles_Username" ON "Profiles" ("Username");

CREATE INDEX "IX_Reactions_EmojiId" ON "Reactions" ("EmojiId");

CREATE INDEX "IX_Reactions_ProfileId" ON "Reactions" ("ProfileId");

CREATE INDEX "IX_SongArtists_ArtistId" ON "SongArtists" ("ArtistId");

CREATE INDEX "IX_SongGenres_GenreId" ON "SongGenres" ("GenreId");

CREATE INDEX "IX_SongLinks_LinkSourceId" ON "SongLinks" ("LinkSourceId");

CREATE INDEX "IX_SongLinks_SongId" ON "SongLinks" ("SongId");

CREATE UNIQUE INDEX "IX_SongLinks_Url" ON "SongLinks" ("Url");

CREATE UNIQUE INDEX "IX_Songs_AppleId" ON "Songs" ("AppleId");

CREATE UNIQUE INDEX "IX_Songs_Isrc" ON "Songs" ("Isrc");

CREATE UNIQUE INDEX "IX_Songs_SpotifyId" ON "Songs" ("SpotifyId");

CREATE INDEX "IX_Songs_Title" ON "Songs" ("Title");

CREATE UNIQUE INDEX "IX_Songs_YoutubeId" ON "Songs" ("YoutubeId");

CREATE INDEX "IX_SotdEntries_AttachmentSongId" ON "SotdEntries" ("AttachmentSongId");

CREATE INDEX "IX_SotdEntries_MessageId" ON "SotdEntries" ("MessageId");

CREATE INDEX "IX_SotdEntries_SongLinkId" ON "SotdEntries" ("SongLinkId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261108042829_Initial', '8.0.33');

COMMIT;

