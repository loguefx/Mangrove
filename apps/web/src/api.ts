// Tiny typed API client. The JWT access token lives in memory only (spec §7); the rotating
// refresh token is an httpOnly cookie the browser sends automatically to /api/auth/*.

export interface UserDto {
  id: number;
  username: string;
  email?: string | null;
  roles: string[];
}

export interface AuthResponse {
  accessToken: string;
  expiresInSeconds: number;
  user: UserDto;
}

export interface SetupStatus {
  setupComplete: boolean;
  appName: string;
}

export interface UpdateStatusDto {
  currentVersion: string;
  latestVersion?: string | null;
  updateAvailable: boolean;
  canSelfUpdate: boolean;
  releaseNotes?: string | null;
  releaseUrl?: string | null;
  publishedAt?: string | null;
  error?: string | null;
}

export interface UpdateApplyResultDto {
  started: boolean;
  message: string;
}

export interface UpdateProgressDto {
  phase: "idle" | "downloading" | "extracting" | "restarting" | "failed";
  percent: number;
  message?: string | null;
  targetVersion?: string | null;
}

export interface LibraryPathDto {
  id: number;
  path: string;
  credentialId?: number | null;
}

export interface LibraryDto {
  id: number;
  name: string;
  type: number;
  storageKind: number;
  rootPath: string;
  credentialId?: number | null;
  folderWatch: boolean;
  lastScanAt?: string | null;
  seriesCount: number;
  paths: LibraryPathDto[];
}

export interface SeriesDto {
  id: number;
  libraryId: number;
  name: string;
  summary?: string | null;
  hasCover: boolean;
  volumeCount: number;
  chapterCount: number;
  readChapters: number;
}

export interface FavoriteUnread {
  seriesId: number;
  seriesName: string;
  hasCover: boolean;
  newChapters: number;
  nextChapterId: number;
  nextChapterNumber: number;
}

export interface ChapterDto {
  id: number;
  number: number;
  title?: string | null;
  pageCount: number;
  fileFormat: string;
  hasCover: boolean;
}

export interface VolumeDto {
  id: number;
  number: number;
  name?: string | null;
  chapters: ChapterDto[];
}

export interface SeriesDetailDto {
  id: number;
  libraryId: number;
  name: string;
  summary?: string | null;
  hasCover: boolean;
  volumes: VolumeDto[];
  genres?: string | null;
  tags?: string | null;
  publisher?: string | null;
  ageRating?: string | null;
  averageRating?: number | null;
  ratingCount: number;
  myStars?: number | null;
  myReview?: string | null;
  wantToRead: boolean;
  language?: string | null;
  writer?: string | null;
  penciller?: string | null;
}

export interface AdminUserDto {
  id: number;
  username: string;
  email?: string | null;
  roles: string[];
  isLocked: boolean;
  createdAt: string;
  lastActiveAt?: string | null;
  libraryIds: number[];
  maxAgeRating?: number | null;
  includeUnknowns: boolean;
}

export interface CollectionDto {
  id: number;
  name: string;
  isPublic: boolean;
  ownerId: number;
  itemCount: number;
}

export interface CollectionDetailDto {
  id: number;
  name: string;
  isPublic: boolean;
  ownerId: number;
  series: SeriesDto[];
}

export interface ReadingListDto {
  id: number;
  name: string;
  isPublic: boolean;
  ownerId: number;
  itemCount: number;
}

export interface ReadingListItemDto {
  id: number;
  chapterId: number;
  order: number;
  seriesName: string;
  chapterNumber: number;
  title?: string | null;
  pageCount: number;
  hasCover: boolean;
}

export interface ReadingListDetailDto {
  id: number;
  name: string;
  isPublic: boolean;
  ownerId: number;
  items: ReadingListItemDto[];
}

export interface ReviewDto {
  userId: number;
  username: string;
  seriesId: number;
  stars: number;
  body?: string | null;
  updatedAt: string;
}

export interface ServerStatsDto {
  users: number;
  libraries: number;
  series: number;
  volumes: number;
  chapters: number;
  totalBytes: number;
  totalPages: number;
}

export interface UserStatsDto {
  chaptersRead: number;
  pagesRead: number;
  inProgress: number;
  wantToReadCount: number;
}

export interface SettingDto {
  key: string;
  value?: string | null;
}

export interface TaskLogDto {
  id: number;
  kind: string;
  target?: string | null;
  status: string;
  message?: string | null;
  startedAt: string;
  finishedAt?: string | null;
}

export interface ActivityDto {
  userId: number;
  username: string;
  seriesId?: number | null;
  seriesName?: string | null;
  chapterId: number;
  chapterNumber: number;
  chapterTitle?: string | null;
  page: number;
  pageCount: number;
  isRead: boolean;
  caughtUp: boolean;
  status: string; // "reading" | "finished" | "caught-up"
  updatedAt: string;
}

// Bump this token whenever the cover pipeline changes so clients stop showing covers that were
// cached under the previous (long-lived) cache rules. Combined with the server's revalidating
// cache headers, this forces a one-time refetch and then relies on ETags for freshness.
export const COVER_CACHE_BUST = "1019";

/** Cover URL for a series, with a global cache-bust token (+ optional per-change version). */
export const seriesCoverUrl = (id: number, v?: number | string) =>
  `/api/series/${id}/cover?cb=${COVER_CACHE_BUST}${v ? `&v=${v}` : ""}`;

/** Cover URL for a chapter, with the same global cache-bust token. */
export const chapterCoverUrl = (id: number) =>
  `/api/chapters/${id}/cover?cb=${COVER_CACHE_BUST}`;

export interface OnlineCandidate {
  aniListId: number;
  title: string;
  year: number | null;
  format: string | null;
  coverUrl: string | null;
  description: string | null;
}

export interface ChapterManifestDto {
  id: number;
  pageCount: number;
  readingDirection: string;
  format: string;
  mediaType: string; // "image" | "epub"
  prevChapterId?: number | null;
  nextChapterId?: number | null;
  chapterLabel?: string | null;
  seriesName?: string | null;
}

export interface EpubSpineItemDto {
  href: string;
  label?: string | null;
}

export interface EpubTocItemDto {
  label: string;
  href: string;
}

export interface EpubManifestDto {
  chapterId: number;
  title: string;
  author?: string | null;
  spine: EpubSpineItemDto[];
  toc: EpubTocItemDto[];
}

export interface SearchResultDto {
  id: number;
  name: string;
  libraryId: number;
  hasCover: boolean;
  kind: string;
}

export interface ContinueReadingDto {
  chapterId: number;
  seriesId: number;
  seriesName: string;
  chapterNumber: number;
  page: number;
  pageCount: number;
  hasCover: boolean;
}

export interface DashboardDto {
  continueReading: ContinueReadingDto[];
  recentlyAdded: SeriesDto[];
}

let accessToken: string | null = null;
let onUnauthorized: (() => void) | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function getAccessToken() {
  return accessToken;
}

export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

async function request<T>(path: string, options: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(options.headers);
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
  // FormData sets its own multipart Content-Type (with boundary); never override it.
  if (options.body && !(options.body instanceof FormData) && !headers.has("Content-Type"))
    headers.set("Content-Type", "application/json");

  const res = await fetch(path, { ...options, headers, credentials: "include" });

  if (res.status === 401 && retry && !path.includes("/api/auth/")) {
    const refreshed = await tryRefresh();
    if (refreshed) return request<T>(path, options, false);
    onUnauthorized?.();
    throw new ApiError(401, "Unauthorized");
  }

  if (!res.ok) {
    let message = res.statusText;
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch {
      /* ignore */
    }
    throw new ApiError(res.status, message);
  }

  if (res.status === 204) return undefined as T;
  const contentType = res.headers.get("Content-Type") ?? "";
  return (contentType.includes("application/json") ? await res.json() : (await res.text())) as T;
}

async function tryRefresh(): Promise<boolean> {
  try {
    const res = await fetch("/api/auth/refresh", { method: "POST", credentials: "include" });
    if (!res.ok) return false;
    const data: AuthResponse = await res.json();
    accessToken = data.accessToken;
    return true;
  } catch {
    return false;
  }
}

export const api = {
  setupStatus: () => request<SetupStatus>("/api/auth/setup-status"),

  registerFirst: (username: string, email: string | null, password: string) =>
    request<AuthResponse>("/api/auth/register-first", {
      method: "POST",
      body: JSON.stringify({ username, email, password }),
    }),

  login: (username: string, password: string) =>
    request<AuthResponse>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),

  refresh: () => request<AuthResponse>("/api/auth/refresh", { method: "POST" }, false),

  logout: () => request<void>("/api/auth/logout", { method: "POST" }),

  me: () => request<UserDto>("/api/me"),

  getPreferences: () => request<Record<string, string>>("/api/me/preferences"),
  savePreferences: (updates: Record<string, string | null>) =>
    request<void>("/api/me/preferences", { method: "PUT", body: JSON.stringify(updates) }),

  libraries: () => request<LibraryDto[]>("/api/libraries"),

  createLibrary: (body: {
    name: string;
    type: number;
    storageKind: number;
    paths: string[];
    credentialId: number | null;
    folderWatch: boolean;
  }) => request<LibraryDto>("/api/libraries", { method: "POST", body: JSON.stringify(body) }),

  updateLibrary: (
    id: number,
    body: { name?: string; folderWatch?: boolean; credentialId?: number | null; paths?: string[] }
  ) => request<LibraryDto>(`/api/libraries/${id}`, { method: "PUT", body: JSON.stringify(body) }),

  deleteLibrary: (id: number) =>
    request<void>(`/api/libraries/${id}`, { method: "DELETE" }),

  scanLibrary: (id: number) =>
    request<{ libraryId: number; state: string; queued: boolean }>(
      `/api/libraries/${id}/scan`,
      { method: "POST" }
    ),

  scanStatus: (id: number) =>
    request<{
      libraryId: number;
      state: string;
      queued: boolean;
      done: number;
      total: number;
      phase: string | null;
    }>(`/api/libraries/${id}/scan-status`),

  series: (
    libraryId: number,
    opts?: { filter?: string; sort?: string; genre?: string; status?: string }
  ) => {
    const qs = new URLSearchParams();
    if (opts?.filter) qs.set("filter", opts.filter);
    if (opts?.sort) qs.set("sort", opts.sort);
    if (opts?.genre) qs.set("genre", opts.genre);
    if (opts?.status) qs.set("status", opts.status);
    const q = qs.toString();
    return request<SeriesDto[]>(`/api/libraries/${libraryId}/series${q ? `?${q}` : ""}`);
  },

  libraryGenres: (libraryId: number) =>
    request<string[]>(`/api/libraries/${libraryId}/genres`),

  seriesDetail: (id: number) => request<SeriesDetailDto>(`/api/series/${id}`),

  manifest: (chapterId: number) => request<ChapterManifestDto>(`/api/chapters/${chapterId}/manifest`),

  bookManifest: (chapterId: number) => request<EpubManifestDto>(`/api/books/${chapterId}/manifest`),

  saveProgress: (chapterId: number, page: number) =>
    request<unknown>("/api/progress", {
      method: "POST",
      body: JSON.stringify({ chapterId, page }),
    }),

  progressForChapter: (chapterId: number) =>
    request<{ chapterId: number; page: number; isRead: boolean }>(`/api/progress/${chapterId}`),

  seriesProgress: (seriesId: number) =>
    request<{ chapterId: number; page: number; isRead: boolean; updatedAt: string }[]>(
      `/api/progress?seriesId=${seriesId}`
    ),

  markChapterRead: (chapterId: number, read: boolean) =>
    request<void>(`/api/progress/chapter/${chapterId}/${read ? "read" : "unread"}`, {
      method: "POST",
    }),

  markSeriesRead: (seriesId: number, read: boolean) =>
    request<void>(`/api/progress/series/${seriesId}/${read ? "read" : "unread"}`, {
      method: "POST",
    }),

  search: (q: string) => request<SearchResultDto[]>(`/api/search?q=${encodeURIComponent(q)}`),

  dashboard: () => request<DashboardDto>("/api/dashboard"),

  updateSeries: (
    id: number,
    body: {
      name?: string;
      summary?: string | null;
      publisher?: string | null;
      language?: string | null;
      genres?: string | null;
      tags?: string | null;
      ageRating?: string | null;
    }
  ) => request<SeriesDetailDto>(`/api/series/${id}`, { method: "PUT", body: JSON.stringify(body) }),

  uploadSeriesCover: (id: number, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return request<SeriesDetailDto>(`/api/series/${id}/cover`, { method: "POST", body: form });
  },

  fetchOnlineMetadata: (id: number, q?: string) =>
    request<{
      matched: boolean;
      summary: string | null;
      genres: string | null;
      tags: string | null;
      writer: string | null;
      penciller: string | null;
      ageRating: string | null;
      coverUrl: string | null;
    }>(`/api/series/${id}/online-metadata${q ? `?q=${encodeURIComponent(q)}` : ""}`, { method: "POST" }),

  applyCoverFromUrl: (id: number, url: string) =>
    request<SeriesDetailDto>(`/api/series/${id}/cover-from-url`, {
      method: "POST",
      body: JSON.stringify({ url }),
    }),

  identifySeries: (id: number, opts: { name?: string; anilistId?: number }) => {
    const p = new URLSearchParams();
    if (opts.name) p.set("name", opts.name);
    if (opts.anilistId) p.set("anilistId", String(opts.anilistId));
    const qs = p.toString();
    return request<OnlineCandidate[]>(`/api/series/${id}/identify${qs ? `?${qs}` : ""}`);
  },

  applyIdentify: (id: number, anilistId: number) =>
    request<SeriesDetailDto>(`/api/series/${id}/identify/${anilistId}`, { method: "POST" }),

  testStorage: (body: {
    storageKind: number;
    rootPath: string;
    username?: string;
    password?: string;
    domain?: string;
  }) =>
    request<{
      success: boolean;
      message: string;
      entries: { name: string; isDirectory: boolean; size: number }[];
    }>("/api/storage/test", { method: "POST", body: JSON.stringify(body) }),

  createCredential: (body: {
    label: string;
    username: string;
    password: string;
    domain?: string | null;
    kind: number;
  }) =>
    request<{ id: number; label: string }>("/api/credentials", {
      method: "POST",
      body: JSON.stringify(body),
    }),

  // ---- Phase 3: users / admin ----
  users: () => request<AdminUserDto[]>("/api/users"),
  createUser: (body: {
    username: string;
    email?: string | null;
    password: string;
    roles?: string[];
    libraryIds?: number[];
  }) => request<AdminUserDto>("/api/users", { method: "POST", body: JSON.stringify(body) }),
  updateUser: (
    id: number,
    body: {
      email?: string | null;
      roles?: string[];
      isLocked?: boolean;
      libraryIds?: number[];
      maxAgeRating?: number | null;
      includeUnknowns?: boolean;
    }
  ) => request<AdminUserDto>(`/api/users/${id}`, { method: "PUT", body: JSON.stringify(body) }),
  resetPassword: (id: number, password: string) =>
    request<void>(`/api/users/${id}/reset-password`, { method: "POST", body: JSON.stringify({ password }) }),
  deleteUser: (id: number) => request<void>(`/api/users/${id}`, { method: "DELETE" }),

  // ---- Phase 3: organize ----
  collections: () => request<CollectionDto[]>("/api/collections"),
  collection: (id: number) => request<CollectionDetailDto>(`/api/collections/${id}`),
  createCollection: (name: string, isPublic: boolean) =>
    request<CollectionDto>("/api/collections", { method: "POST", body: JSON.stringify({ name, isPublic }) }),
  updateCollection: (id: number, name: string, isPublic: boolean) =>
    request<CollectionDto>(`/api/collections/${id}`, { method: "PUT", body: JSON.stringify({ name, isPublic }) }),
  deleteCollection: (id: number) => request<void>(`/api/collections/${id}`, { method: "DELETE" }),
  addToCollection: (id: number, seriesId: number) =>
    request<void>(`/api/collections/${id}/items/${seriesId}`, { method: "POST" }),
  removeFromCollection: (id: number, seriesId: number) =>
    request<void>(`/api/collections/${id}/items/${seriesId}`, { method: "DELETE" }),

  readingLists: () => request<ReadingListDto[]>("/api/reading-lists"),
  readingList: (id: number) => request<ReadingListDetailDto>(`/api/reading-lists/${id}`),
  createReadingList: (name: string, isPublic: boolean) =>
    request<ReadingListDto>("/api/reading-lists", { method: "POST", body: JSON.stringify({ name, isPublic }) }),
  deleteReadingList: (id: number) => request<void>(`/api/reading-lists/${id}`, { method: "DELETE" }),
  addToReadingList: (id: number, chapterId: number) =>
    request<void>(`/api/reading-lists/${id}/items/${chapterId}`, { method: "POST" }),
  removeFromReadingList: (id: number, itemId: number) =>
    request<void>(`/api/reading-lists/${id}/items/${itemId}`, { method: "DELETE" }),
  reorderReadingList: (id: number, itemIds: number[]) =>
    request<void>(`/api/reading-lists/${id}/reorder`, { method: "PUT", body: JSON.stringify({ itemIds }) }),
  importCbl: (name: string | null, xml: string) =>
    request<{ listId: number; name: string; matched: number; unmatched: number }>(
      "/api/reading-lists/import-cbl",
      { method: "POST", body: JSON.stringify({ name, xml }) }
    ),

  wantToRead: () => request<SeriesDto[]>("/api/want-to-read"),
  favoritesUnread: () => request<FavoriteUnread[]>("/api/want-to-read/unread"),
  addWantToRead: (seriesId: number) => request<void>(`/api/want-to-read/${seriesId}`, { method: "POST" }),
  removeWantToRead: (seriesId: number) => request<void>(`/api/want-to-read/${seriesId}`, { method: "DELETE" }),

  rate: (seriesId: number, stars: number) =>
    request<void>("/api/ratings", { method: "POST", body: JSON.stringify({ seriesId, stars }) }),
  review: (seriesId: number, body: string | null) =>
    request<void>("/api/reviews", { method: "POST", body: JSON.stringify({ seriesId, body }) }),
  reviews: (seriesId: number) => request<ReviewDto[]>(`/api/reviews?seriesId=${seriesId}`),

  // ---- Phase 3: stats / settings / tasks ----
  serverStats: () => request<ServerStatsDto>("/api/stats/server"),
  myStats: () => request<UserStatsDto>("/api/stats/me"),
  settings: () => request<SettingDto[]>("/api/settings"),
  saveSettings: (settings: SettingDto[]) =>
    request<void>("/api/settings", { method: "PUT", body: JSON.stringify(settings) }),
  tasks: () => request<TaskLogDto[]>("/api/tasks"),

  activity: () => request<ActivityDto[]>("/api/stats/activity"),
  scanAll: () =>
    request<{ libraries: number; status: string }>("/api/tasks/scan-all", { method: "POST" }),

  repairCovers: () =>
    request<{ status: string }>("/api/tasks/repair-covers", { method: "POST" }),

  // ---- Server updates (admin) ----
  updateStatus: () => request<UpdateStatusDto>("/api/admin/update/status"),
  applyUpdate: () => request<UpdateApplyResultDto>("/api/admin/update/apply", { method: "POST" }),
  updateProgress: () => request<UpdateProgressDto>("/api/admin/update/progress"),
};

async function authedFetch(path: string): Promise<Response> {
  const headers = new Headers();
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
  let res = await fetch(path, { headers, credentials: "include" });
  if (res.status === 401 && (await tryRefresh())) {
    headers.set("Authorization", `Bearer ${accessToken}`);
    res = await fetch(path, { headers, credentials: "include" });
  }
  return res;
}

/**
 * Best-effort warm of an authenticated binary endpoint. Consuming the body populates the browser's
 * HTTP cache (pages are sent with a long max-age) and, more importantly, warms the server's archive
 * cache so the expensive first read over the NAS happens *before* the user navigates there.
 */
export async function prefetchAuthed(path: string): Promise<void> {
  try {
    const res = await authedFetch(path);
    if (res.ok) await res.blob();
  } catch {
    /* prefetch is best-effort; ignore failures */
  }
}

/** Fetches an authenticated binary endpoint (e.g. a reader page) as an object URL. */
export async function fetchImageObjectUrl(path: string): Promise<string> {
  const res = await authedFetch(path);
  if (!res.ok) throw new ApiError(res.status, `Failed to load image (${res.status})`);
  return URL.createObjectURL(await res.blob());
}

/** Fetches an authenticated endpoint as text (e.g. an EPUB spine document). */
export async function fetchTextWithAuth(path: string): Promise<string> {
  const res = await authedFetch(path);
  if (!res.ok) throw new ApiError(res.status, `Failed to load resource (${res.status})`);
  return res.text();
}

/** Fetches an authenticated endpoint as an object URL for arbitrary binary (epub images/fonts). */
export async function fetchBlobObjectUrl(path: string): Promise<string> {
  const res = await authedFetch(path);
  if (!res.ok) throw new ApiError(res.status, `Failed to load resource (${res.status})`);
  return URL.createObjectURL(await res.blob());
}

/** Builds the authenticated content URL for an EPUB resource by href. */
export function bookContentUrl(chapterId: number, href: string): string {
  const encoded = href.split("/").map(encodeURIComponent).join("/");
  return `/api/books/${chapterId}/content/${encoded}`;
}
