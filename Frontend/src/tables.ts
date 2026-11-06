// The shape of every table the CRUD tab can edit. This mirrors Database/spec.md: which tables are big
// enough to page, what each table is ordered by to begin with, and which columns can be edited.

export type ColumnKind = 'guid' | 'text' | 'snowflake' | 'bool' | 'int' | 'datetime' | 'image';

export interface ColumnDef {
  key: string;
  label: string;
  kind: ColumnKind;
  /** Primary keys and derived columns are shown greyed out and cannot be changed. */
  editable: boolean;
  /** Nullable columns may be left empty in the create form and cleared in the table. */
  nullable?: boolean;
}

export interface TableDef {
  path: string;
  label: string;
  /** Big tables page through the API; small tables load in one go. */
  big: boolean;
  defaultOrderBy: string;
  defaultDesc: boolean;
  columns: ColumnDef[];
}

const id: ColumnDef = { key: 'id', label: 'ID', kind: 'guid', editable: false };

export const TABLES: TableDef[] = [
  {
    path: 'people',
    label: 'Person',
    big: false,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [id, { key: 'name', label: 'Name', kind: 'text', editable: true }],
  },
  {
    path: 'profiles',
    label: 'Profile',
    big: false,
    defaultOrderBy: 'username',
    defaultDesc: false,
    columns: [
      id,
      { key: 'discordId', label: 'Discord User ID', kind: 'snowflake', editable: true },
      { key: 'username', label: 'Username', kind: 'text', editable: true },
      { key: 'isCurrentMember', label: 'Is Current Member', kind: 'bool', editable: true },
      { key: 'picturePath', label: 'Profile Picture', kind: 'image', editable: true },
    ],
  },
  {
    path: 'channels',
    label: 'Channel',
    big: false,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [
      id,
      { key: 'discordId', label: 'Discord Channel ID', kind: 'snowflake', editable: true },
      { key: 'name', label: 'Name', kind: 'text', editable: true },
    ],
  },
  {
    path: 'messages',
    label: 'Message',
    big: true,
    defaultOrderBy: 'postedAt',
    defaultDesc: true,
    columns: [
      id,
      { key: 'discordId', label: 'Discord Message ID', kind: 'snowflake', editable: true },
      { key: 'channelId', label: 'Channel', kind: 'guid', editable: true },
      { key: 'postedById', label: 'Posted By', kind: 'guid', editable: true },
      { key: 'postedAt', label: 'Posted At', kind: 'datetime', editable: true },
      { key: 'editedAt', label: 'Edited At', kind: 'datetime', editable: true, nullable: true },
      { key: 'content', label: 'Content', kind: 'text', editable: true },
      { key: 'replyId', label: 'Reply To', kind: 'guid', editable: true, nullable: true },
    ],
  },
  {
    path: 'file-extensions',
    label: 'File Extension',
    big: false,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [id, { key: 'name', label: 'Name', kind: 'text', editable: true }],
  },
  {
    path: 'attachments',
    label: 'Attachment',
    big: true,
    defaultOrderBy: 'filename',
    defaultDesc: false,
    columns: [
      id,
      { key: 'discordId', label: 'Discord ID', kind: 'snowflake', editable: true },
      { key: 'messageId', label: 'Message', kind: 'guid', editable: true },
      { key: 'filename', label: 'Filename', kind: 'text', editable: true },
      { key: 'fileExtensionId', label: 'File Extension', kind: 'guid', editable: true },
      { key: 'size', label: 'Size', kind: 'int', editable: true },
      { key: 'path', label: 'Path', kind: 'text', editable: true },
    ],
  },
  {
    path: 'emojis',
    label: 'Emoji',
    big: false,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [
      id,
      { key: 'name', label: 'Name', kind: 'text', editable: true },
      { key: 'discordId', label: 'Discord ID', kind: 'snowflake', editable: true, nullable: true },
      { key: 'isAnimated', label: 'Is Animated', kind: 'bool', editable: true },
      { key: 'path', label: 'Path', kind: 'text', editable: true, nullable: true },
    ],
  },
  {
    path: 'songs',
    label: 'Song',
    big: true,
    defaultOrderBy: 'title',
    defaultDesc: false,
    columns: [
      id,
      { key: 'title', label: 'Title', kind: 'text', editable: true },
      { key: 'spotifyId', label: 'Spotify ID', kind: 'text', editable: true, nullable: true },
      { key: 'isrc', label: 'ISRC', kind: 'text', editable: true, nullable: true },
    ],
  },
  {
    path: 'genres',
    label: 'Genre',
    big: true,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [
      id,
      { key: 'name', label: 'Name', kind: 'text', editable: true },
      { key: 'parentId', label: 'Parent', kind: 'guid', editable: true, nullable: true },
    ],
  },
  {
    path: 'artists',
    label: 'Artist',
    big: true,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [
      id,
      { key: 'name', label: 'Name', kind: 'text', editable: true },
      { key: 'spotifyId', label: 'Spotify ID', kind: 'text', editable: true, nullable: true },
    ],
  },
  {
    path: 'sources',
    label: 'Source',
    big: false,
    defaultOrderBy: 'name',
    defaultDesc: false,
    columns: [id, { key: 'name', label: 'Name', kind: 'text', editable: true }],
  },
  {
    path: 'sotd-entries',
    label: 'SOTD Entry',
    big: true,
    defaultOrderBy: 'postedAt',
    defaultDesc: true,
    columns: [
      id,
      { key: 'songId', label: 'Song', kind: 'guid', editable: true },
      { key: 'messageId', label: 'Message', kind: 'guid', editable: true },
      { key: 'sourceId', label: 'Source', kind: 'guid', editable: true },
      { key: 'link', label: 'Link', kind: 'text', editable: true, nullable: true },
      { key: 'attachmentId', label: 'Attachment', kind: 'guid', editable: true, nullable: true },
      { key: 'duration', label: 'Duration (s)', kind: 'int', editable: true },
      { key: 'releaseDate', label: 'Release Date', kind: 'datetime', editable: true, nullable: true },
      // Comes from the message the entry belongs to, which is what the table is ordered by.
      { key: 'postedAt', label: 'Posted At', kind: 'datetime', editable: false },
    ],
  },
];

/** Turns a row's value into the string shown in a cell and written to the CSV. */
export function display(value: unknown, kind: ColumnKind): string {
  if (value === null || value === undefined) return '';
  if (kind === 'bool') return value ? 'Yes' : 'No';
  if (kind === 'datetime') {
    const date = new Date(String(value));
    return Number.isNaN(date.getTime()) ? String(value) : date.toISOString().replace('T', ' ').slice(0, 19);
  }
  return String(value);
}

/** Converts what was typed in a cell or form field back into what the API expects. */
export function parseInput(text: string, column: ColumnDef): unknown {
  const trimmed = text.trim();
  if (trimmed === '') return column.nullable ? null : column.kind === 'int' ? 0 : '';
  switch (column.kind) {
    case 'int':
      return Number(trimmed);
    case 'bool':
      return /^(true|yes|1)$/i.test(trimmed);
    case 'datetime':
      return new Date(trimmed).toISOString();
    default:
      return trimmed;
  }
}

/** The payload for create and update: every editable column, keyed as the API names it. */
export function toWrite(table: TableDef, row: Record<string, unknown>): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  for (const column of table.columns) {
    if (!column.editable) continue;
    body[column.key] = row[column.key] ?? (column.nullable ? null : column.kind === 'bool' ? false : '');
  }
  return body;
}
