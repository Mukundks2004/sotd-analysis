// Everything the UI knows about talking to the backend. Failures always come back as a thrown ApiError so
// that each caller can put the message straight into a toast.

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export class ApiError extends Error {
  readonly detail?: string;
  constructor(message: string, detail?: string) {
    super(message);
    this.name = 'ApiError';
    this.detail = detail;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      ...init,
      headers: init?.body ? { 'Content-Type': 'application/json', ...init?.headers } : init?.headers,
    });
  } catch {
    throw new ApiError('Could not reach the API. Is the backend running?');
  }

  if (response.status === 204) return undefined as T;

  const text = await response.text();
  const body = text ? safeParse(text) : undefined;

  if (!response.ok) {
    throw new ApiError(messageFrom(body) ?? `Request failed (${response.status}).`, detailFrom(body));
  }
  return body as T;
}

function safeParse(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return { message: text };
  }
}

function messageFrom(body: unknown): string | undefined {
  if (!body || typeof body !== 'object') return undefined;
  const record = body as Record<string, unknown>;
  if (typeof record.message === 'string') return record.message;
  // ASP.NET's own model-validation problem details, for when a field cannot even be parsed.
  if (record.errors && typeof record.errors === 'object') {
    const all = Object.values(record.errors as Record<string, string[]>).flat();
    if (all.length > 0) return all.join(' ');
  }
  if (typeof record.title === 'string') return record.title;
  return undefined;
}

function detailFrom(body: unknown): string | undefined {
  if (!body || typeof body !== 'object') return undefined;
  const detail = (body as Record<string, unknown>).detail;
  return typeof detail === 'string' ? detail : undefined;
}

export interface ListParams {
  orderBy?: string;
  desc?: boolean;
  filter?: string;
  page?: number;
  pageSize?: number;
}

function queryString(params: ListParams): string {
  const search = new URLSearchParams();
  if (params.orderBy) search.set('orderBy', params.orderBy);
  if (params.desc !== undefined) search.set('desc', String(params.desc));
  if (params.filter) search.set('filter', params.filter);
  if (params.page) search.set('page', String(params.page));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  const value = search.toString();
  return value ? `?${value}` : '';
}

export type Row = Record<string, unknown> & { id: string };

export const api = {
  health: () => request<{ database: string; exists: boolean; songs: number; entries: number }>('/health'),

  list: (path: string, params: ListParams) => request<Paged<Row>>(`/${path}${queryString(params)}`),

  /**
   * Every row matching a filter and sort, which is what CSV export and printing need. The list endpoints
   * only accept page sizes of 10, 20 or 50, so walk the pages at the largest of those.
   */
  listAll: async (path: string, params: ListParams): Promise<Row[]> => {
    const rows: Row[] = [];
    for (let page = 1; ; page++) {
      const result = await request<Paged<Row>>(`/${path}${queryString({ ...params, page, pageSize: 50 })}`);
      rows.push(...result.items);
      // Small tables ignore paging and hand back everything at once.
      if (rows.length >= result.total || result.items.length === 0 || page >= result.totalPages) break;
    }
    return rows;
  },

  create: (path: string, body: unknown) => request<Row>(`/${path}`, { method: 'POST', body: JSON.stringify(body) }),

  update: (path: string, id: string, body: unknown) =>
    request<Row>(`/${path}/${id}`, { method: 'PUT', body: JSON.stringify(body) }),

  remove: (path: string, ids: string[]) =>
    request<void>(`/${path}/delete`, { method: 'POST', body: JSON.stringify({ ids }) }),

  // Many-to-many annotation.
  relationships: () =>
    request<
      { key: string; label: string; owner: string; tag: string; ordered: boolean; big: boolean }[]
    >('/annotations/relationships'),

  annotationRows: (relationship: string, params: ListParams) =>
    request<Paged<AnnotationRow>>(`/annotations/${relationship}${queryString(params)}`),

  tagOptions: (relationship: string, q: string) =>
    request<Tag[]>(`/annotations/${relationship}/options${q ? `?q=${encodeURIComponent(q)}` : ''}`),

  addTag: (relationship: string, ownerId: string, tag: { tagId?: string; newName?: string }) =>
    request<Tag>(`/annotations/${relationship}/tags`, {
      method: 'POST',
      body: JSON.stringify({ ownerId, ...tag }),
    }),

  removeTag: (relationship: string, ownerId: string, tagId: string) =>
    request<void>(`/annotations/${relationship}/tags?ownerId=${ownerId}&tagId=${tagId}`, { method: 'DELETE' }),

  reorderArtists: (songId: string, artistIds: string[]) =>
    request<Tag[]>('/annotations/song-artists/order', {
      method: 'PUT',
      body: JSON.stringify({ songId, artistIds }),
    }),

  // Genre hierarchy.
  genreGraph: () => request<GenreGraph>('/genre-graph'),

  createGenre: (name: string) =>
    request<GenreNode>('/genre-graph', { method: 'POST', body: JSON.stringify({ name }) }),

  setGenreParent: (parentId: string, childId: string) =>
    request<GenreGraph>('/genre-graph/edges', { method: 'PUT', body: JSON.stringify({ parentId, childId }) }),

  removeGenreParent: (childId: string) =>
    request<GenreGraph>(`/genre-graph/edges?childId=${childId}`, { method: 'DELETE' }),
};

export interface Tag {
  id: string;
  name: string;
}

export interface AnnotationRow {
  id: string;
  label: string;
  detail: string | null;
  tags: Tag[];
}

export interface GenreNode {
  id: string;
  name: string;
}

export interface GenreEdge {
  parentId: string;
  childId: string;
}

export interface GenreGraph {
  nodes: GenreNode[];
  edges: GenreEdge[];
}
