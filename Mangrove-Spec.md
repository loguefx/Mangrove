# Mangrove — Self-Hosted Reading Server

**Tagline:** *Your whole library, rooted in one place.*

Mangrove is a self-hosted server for manga, webtoons, comics, and books. It reads
your files directly from network shares (UNC / SMB) without mounting any drives,
serves them through a clean web UI, and pairs with a native Android app. It aims for
full feature parity with Kavita, plus first-class SMB/UNC storage and an offline-capable
mobile app.

> The name lives in one constant (`APP_NAME`) so it can be changed everywhere at once.

---

## 0. Cursor kickoff prompt (paste this first)

> You are building **Mangrove**, a self-hosted reading server (manga, comics, webtoons,
> books) with a clean web UI and a native Android app. Build it as a monorepo. Use the
> stack and architecture exactly as described in this document. Work in phases — finish
> Phase 1 end-to-end (it must run, scan a library over SMB, authenticate a user, and read
> a CBZ in the browser) before moving on. After each phase, give me run instructions and
> a short summary of what changed. Do not stub the SMB layer — wire it to a real SMB
> client library. Ask me before introducing any paid/premium service. Keep secrets in
> environment variables and never commit them. Generate tests for the scanner, the SMB
> path resolver, and the auth flow. Match the data model, REST endpoints, and folder
> layout defined below; if you deviate, tell me why.

Start with: scaffold the monorepo, set up the backend project, create the SQLite schema
and first-run admin setup, then implement the SMB storage provider and the library scanner.

---

## 1. Goals and non-goals

### Goals
- Serve manga / webtoons / comics / books with format-specific readers.
- Read content **directly from SMB / UNC paths** with stored credentials — no OS mounts.
- Username + password auth backed by **SQLite**, with roles and per-library access.
- A clean, fast, responsive web UI with a branded login page.
- A native **Android app** that connects to the server, with offline downloads, that
  ships as an installable **APK**.
- Feature parity with Kavita (see §13 checklist).

### Non-goals (v1)
- Acquiring or downloading content from the internet (no scrapers / *arr-style automation).
- Audiobooks / video.
- Paid tiers. Everything is free and self-hosted. External metadata enrichment is optional
  and off by default.

> Mangrove manages files **you already own and have the right to store**. It is a reader/
> organizer, like Kavita or Komga — not a content source.

---

## 2. Recommended tech stack

**Primary stack (recommended — best format + SMB + image handling, closest to Kavita):**

| Layer | Choice | Why |
|---|---|---|
| Backend | **ASP.NET Core 8 (C#)** Web API | Strong archive + image + SMB ecosystem; mirrors Kavita |
| Database | **SQLite** via **EF Core** | Single-file, zero-config, exactly what was requested |
| Auth | ASP.NET Identity + **JWT** (access + refresh) | Battle-tested; clean API for the Android app |
| Archives | **SharpCompress** | zip, cbz, rar/rar5, cbr, 7z/cb7, tar, tar.gz (read) |
| Images | **SkiaSharp** (+ ImageSharp fallback) | thumbnails, covers, webp/avif decode, page transforms |
| EPUB | **VersOne.Epub** | epub2/epub3 parsing + spine streaming |
| PDF | **PdfPig** + **PDFtoImage/Docnet** | text + page rasterization |
| SMB / UNC | **SMBLibrary** (TalAloni) | pure-C# SMB1/2/3 client — read shares with **no mount** |
| OPDS | hand-rolled OPDS 1.2 feed | lets existing readers (KOReader, Panels, Mihon, etc.) connect too |
| Web UI | **React + TypeScript + Vite + Tailwind + shadcn/ui** | clean, fast, component-driven |
| Android | **Kotlin + Jetpack Compose**, Retrofit/OkHttp, Coil, Room, DataStore | native feel, easy `.apk` via Gradle |
| Container | **Docker** (multi-arch x86_64 + arm64) | one-command self-host |

**Alternative all-JS stack** (use only if you prefer a single language): NestJS (Node/TS)
backend with `@marsaud/smb2` for SMB, `node-unrar-js`/`7zip-min` for archives, `sharp` for
images; React Native (Expo) for mobile. Note: Node's SMB and RAR support are weaker than the
.NET options, so the primary stack is preferred. **The rest of this document assumes the
primary stack.**

---

## 3. Architecture

```
                         ┌──────────────────────────────┐
   Android app  ───────► │  Mangrove API (ASP.NET Core)  │
   Web UI (SPA) ───────► │   - Auth (JWT, SQLite)        │
   OPDS readers ───────► │   - Library / scan service    │
                         │   - Reader streaming endpoints │
                         │   - SMB storage provider ──────┼──► \\NAS\Manga  (SMB/UNC)
                         │   - Metadata / cover cache     │     \\NAS\Books
                         └───────────────┬────────────────┘     (no OS mount)
                                         │
                                  SQLite (mangrove.db)
                                  + cover/thumb cache dir
```

Key idea: a `IStorageProvider` abstraction. Implementations: `LocalStorageProvider`
(local/mounted paths) and `SmbStorageProvider` (SMBLibrary). The scanner, cover extractor,
and reader endpoints **only** talk to `IStorageProvider`, so SMB vs local is transparent.

---

## 4. Supported formats

Match Kavita and add a couple of common extras.

**Manga / Comics / Webtoons (archives + raw images):**
`.cbz .cbr .cb7 .cbt .zip .rar .7z .tar .tar.gz` and raw image folders
(`.jpg .jpeg .png .webp .gif .avif .bmp`).

**Books:**
`.epub` (epub2 + epub3), `.pdf`. *Stretch:* `.mobi` / `.azw3` (convert-on-read), `.txt`,
`.cbz`-as-comic-book.

**Metadata sidecars:** `ComicInfo.xml` (comics/manga) and EPUB OPF metadata. Optionally read
`.opf` and series-level `series.json`.

Format detection is by signature + extension, not extension alone. A central
`FormatRegistry` maps extension/signature → media type → reader.

---

## 5. SMB / UNC storage (the headline feature)

Requirement: point a library at `\\SERVER\Share\Path` (or `smb://server/share/path`) and
read it **without mounting** anything on the host OS.

**Design**
- `SmbStorageProvider` wraps **SMBLibrary**. It opens an authenticated session per share
  using stored credentials and exposes: `List(path)`, `Stat(path)`, `OpenRead(path)`
  (returns a seekable/streaming `Stream`), and `Watch(path)` (poll-based change detection
  since SMB change-notify is unreliable across servers).
- **Path model:** a `StoragePath` value object parses both UNC (`\\host\share\dir\file`)
  and `smb://` URIs into `{ host, share, relativePath }`. Never pass raw OS paths around.
- **Connection pooling:** reuse SMB sessions keyed by `{host, share, credentialId}`;
  reconnect with backoff on `STATUS_*` session errors. Cap concurrent reads per share.
- **Streaming reads:** reader endpoints stream pages straight from the SMB `Stream` →
  archive entry → HTTP response. Cache extracted covers/thumbnails on local disk so we
  don't re-hit the share for browse views.
- **Credentials:** stored in SQLite, encrypted at rest (ASP.NET Data Protection / AES-GCM
  with a key from env). Support domain, anonymous/guest, and SMB2/3.
- **Settings UI:** add-library flow lets the user pick `Local` or `SMB`. For SMB: host,
  share, path, username, password, domain (optional), SMB dialect (auto/2/3), and a
  **Test Connection** button that lists the top-level folder before saving.

**Acceptance test:** configure an SMB library pointing at a NAS share with sample CBZ +
EPUB files, run a scan, browse covers, and read a chapter — with nothing mounted on the host.

---

## 6. Data model (SQLite via EF Core)

Entities (tables) — adjust column names as idiomatic, keep the shape:

- **User** — `Id, Username (unique), Email?, PasswordHash, CreatedAt, LastActiveAt, IsLocked`
- **Role** — `Admin, User, ReadOnly` (+ flags: `canDownload, canManageLibraries`)
- **UserRole** — join
- **AgeRestriction** — `UserId, MaxAgeRating, IncludeUnknowns(bool)`
- **Credential** — `Id, Label, Username, PasswordEnc, Domain?, Kind(Local|Smb)`
- **Library** — `Id, Name, Type(Manga|Comic|Book|Mixed), StorageKind(Local|Smb),
  RootPath, CredentialId?, FolderWatch(bool), LastScanAt`
- **LibraryAccess** — join `UserId, LibraryId`
- **Series** — `Id, LibraryId, Name, SortName, LocalizedName?, CoverPath, AgeRating,
  Summary?, Publisher?, Language?, Genres, Tags, People(json), ExternalIds(json)`
- **Volume** — `Id, SeriesId, Number, Name?`
- **Chapter** — `Id, VolumeId, Number, Title?, PageCount, FileFormat, Range`
- **MangaFile** — `Id, ChapterId, StoragePath, Bytes, Format, Hash, LastModified`
- **Page** *(optional cache)* — `ChapterId, Index, Width, Height`
- **ReadingProgress** — `UserId, ChapterId, PageNum, ScrollOffset?, UpdatedAt, IsRead`
- **Bookmark** — `UserId, ChapterId, PageNum, CreatedAt`
- **Annotation** *(epub)* — `UserId, ChapterId, Cfi/Range, Note?, Color, CreatedAt`
- **Collection** — `Id, OwnerId, Name, IsPublic` + **CollectionItem** (series)
- **ReadingList** — ordered list + **ReadingListItem** (chapter-level; CBL import)
- **WantToRead** — `UserId, SeriesId`
- **Rating / Review** — `UserId, SeriesId, Stars, Body?`
- **AppSetting** — key/value (port, base URL, theme defaults, SMTP, OPDS toggle)
- **ScanTask / JobLog** — recurring + ad-hoc task history

First-run: no seeded credentials. A **setup wizard** creates the first Admin (matches
Kavita's "no default password" behavior).

---

## 7. Authentication and users

- **Login page** at `/login`, branded with the Mangrove logo (use `mangrove-icon.svg`
  centered above the form; `mangrove-wordmark.svg` for the header).
- Username + password (no spaces in usernames). Argon2id or bcrypt hashing.
- **JWT** access token (short-lived) + refresh token (rotating, stored hashed). Android app
  stores tokens in encrypted DataStore; web stores in memory + httpOnly refresh cookie.
- **Roles:** Admin (manage everything), User (read + own lists), ReadOnly. Per-library
  access via `LibraryAccess`. **Age restrictions** filter series by `AgeRating`.
- Admin invite flow + password reset (email optional via SMTP).
- *Optional, off by default:* OIDC/SSO (parity with Kavita) — design the auth layer so an
  OIDC provider can be added without reworking it.
- Rate-limit login, lockout after N failures, audit log of admin actions.

---

## 8. Library scanning and metadata

- **Scanner:** walks the library root via `IStorageProvider`, groups files into
  Series → Volume → Chapter using filename parsing (volume/chapter/issue regex, same spirit
  as Kavita). Idempotent: skip files whose `hash`/`lastModified` is unchanged (no needless
  I/O). Ad-hoc + scheduled scans; **folder watching** (poll for SMB) auto-imports changes.
- **Covers/thumbnails:** extract first image (archives) or cover (epub/pdf) → resize →
  cache to local disk; serve cached, never re-extract on browse.
- **Metadata:** parse `ComicInfo.xml` and EPUB OPF (title, series, number, summary, writer/
  penciller, genres, tags, publisher, language, age rating, release date). User edits stored
  in DB and win over file metadata.
- **Mixed libraries** allowed (light novels next to manga).
- *Optional external metadata* (AniList/MAL/Comic Vine) behind an explicit toggle, off by
  default, with the user's own API key — never required.

---

## 9. REST API surface (v1)

JSON, JWT-protected except auth + OPDS-with-basic-auth. Prefix `/api`.

```
POST   /api/auth/register-first        # only valid until first admin exists
POST   /api/auth/login                 # -> access + refresh
POST   /api/auth/refresh
POST   /api/auth/logout
GET    /api/me

# admin
GET/POST/PUT/DELETE /api/users
GET/POST/PUT/DELETE /api/credentials
GET/POST/PUT/DELETE /api/libraries
POST   /api/libraries/{id}/scan
POST   /api/storage/test               # test SMB/local connection (host, share, creds)
GET    /api/settings  | PUT /api/settings

# browse
GET    /api/libraries
GET    /api/libraries/{id}/series?filter=&sort=&page=
GET    /api/series/{id}
GET    /api/series/{id}/volumes
GET    /api/volumes/{id}/chapters
GET    /api/chapters/{id}
GET    /api/search?q=

# reading (streaming)
GET    /api/chapters/{id}/cover
GET    /api/chapters/{id}/pages/{n}            # image page (with width/quality params)
GET    /api/chapters/{id}/manifest             # page count, dims, reading direction
GET    /api/books/{chapterId}/content/{href}   # epub resource streaming
GET    /api/chapters/{id}/download             # if canDownload
POST   /api/progress                           # {chapterId, page, scrollOffset}
GET    /api/progress?seriesId=

# organize
GET/POST/PUT/DELETE /api/collections
GET/POST/PUT/DELETE /api/reading-lists         # + CBL import
POST   /api/want-to-read/{seriesId}
POST   /api/ratings  | POST /api/reviews
POST   /api/bookmarks | POST /api/annotations

# interop
GET    /api/opds/...                            # OPDS 1.2 feed (basic auth)
GET    /api/stats/server | /api/stats/me
```

Stream pages with HTTP range support + `Cache-Control` so the Android reader and web reader
can prefetch smoothly.

---

## 10. Web UI

**Design language (clean, calm, content-first):**
- Color: ink/teal from the logo. Surfaces near-white in light mode, near-black in dark mode;
  Mangrove teal (`#0D9488` / accent `#2DD4BF`) for primary actions. Generous whitespace,
  one accent color, rounded-`2xl` cards, soft shadows, no clutter.
- Typography: one clean sans (e.g. Inter). Covers do the visual work; chrome stays quiet.
- Layout: left rail (libraries, collections, want-to-read, settings) + content grid of cover
  cards with hover progress bars. Sticky search. Light/dark toggle. Fully responsive.

**Pages:** Login (branded) · Setup wizard · Dashboard (continue reading, recently added,
on deck) · Library grid (filter/sort/smart filters) · Series detail (volumes/chapters,
metadata, rate/review, want-to-read) · Readers · Collections / Reading lists · Search
results · Admin (users, libraries, credentials, tasks, stats, settings) · Account prefs.

**Readers (match Kavita's modes):**
- *Image reader* (manga/comic): single page, **dual-page** spread (with optional book-shadow),
  **webtoon/continuous scroll**, fit-width/height/original, image splitting for joined
  spreads, reading direction LTR/RTL and vertical, fullscreen, read across files without
  leaving the reader, page bookmarks, prefetch/caching.
- *EPUB reader:* paginated virtual pages or scroll, font/size/spacing/margin/theme controls,
  dark/black/white themes persisted, immersive mode, highlights + notes (annotations),
  table of contents.
- *PDF reader:* page navigation, fit modes, continuous scroll.
- Progress saved per page/scroll-offset; resume from dashboard.

---

## 11. Android app

**Stack:** Kotlin, Jetpack Compose, Material 3, Retrofit + OkHttp + kotlinx-serialization,
Coil (image loading/caching), Room (offline library + downloads), DataStore (server URL +
encrypted tokens), WorkManager (background downloads/sync).

**Connect flow:** first screen asks for **Server URL** (e.g. `https://mangrove.mylan`),
then the branded **login** (username/password) hitting `/api/auth/login`. Token refresh is
automatic via an OkHttp authenticator. Supports multiple saved servers.

**Screens:** Server picker → Login → Home (continue reading / recently added) → Library grid
→ Series detail → Reader. Settings: theme, reader defaults, download-over-wifi-only, cache size.

**Reader:** Compose `HorizontalPager` for paged manga/comics (LTR/RTL/dual-page), `LazyColumn`
for webtoon continuous scroll, fit modes, tap zones, immersive fullscreen. EPUB via a
WebView-based renderer (or `epub4j` to extract + render). Progress syncs to `/api/progress`.

**Offline:** download a chapter/volume → store the archive (or extracted pages) in app storage,
tracked in Room → read with no connection; queued progress syncs when back online. Honors
`canDownload`.

**Building the APK:**
```bash
cd apps/android
./gradlew assembleDebug      # -> app/build/outputs/apk/debug/app-debug.apk  (sideload-ready)
./gradlew assembleRelease    # signed release APK (configure signing below)
```
Add a release signing config in `app/build.gradle.kts` reading keystore details from
`local.properties`/env (never commit the keystore):
```
signingConfigs { create("release") { storeFile=file(System.getenv("MANGROVE_KEYSTORE"))
  storePassword=System.getenv("MANGROVE_STORE_PW"); keyAlias=System.getenv("MANGROVE_KEY_ALIAS")
  keyPassword=System.getenv("MANGROVE_KEY_PW") } }
```
Generate a keystore once with `keytool -genkeypair -v -keystore mangrove.jks -alias mangrove
-keyalg RSA -keysize 2048 -validity 10000`. Ship the resulting `app-release.apk` for sideloading.
Use the icon (`mangrove-icon.svg`) to generate launcher icons (`mipmap` + adaptive icon).

---

## 12. Build, run, deploy

**Dev**
```bash
# backend
cd apps/server && dotnet run            # http://localhost:5000, creates mangrove.db on first run
# web
cd apps/web && npm install && npm run dev
# android
cd apps/android && ./gradlew assembleDebug
```

**Docker (self-host)** — multi-arch image, persistent volumes for `mangrove.db` and the
cover/thumb cache. SMB libraries need **no** host mounts; only add a host mount if you choose
a `Local` library. Expose port 5000 and put it behind your reverse proxy for remote access.

Config via env: `MANGROVE_PORT`, `MANGROVE_DB_PATH`, `MANGROVE_CACHE_DIR`,
`MANGROVE_DATAPROTECTION_KEY`, `MANGROVE_JWT_SECRET`, optional SMTP vars.

---

## 13. Kavita parity checklist

- [ ] Formats: cbz/cbr/cb7/cbt/zip/rar/rar5/7z/tar.gz + raw images; epub2/3; pdf
- [ ] Image reader: single / dual-page (+shadow) / webtoon continuous / fit modes / split / LTR-RTL-vertical / fullscreen / read across files / page bookmarks / caching
- [ ] EPUB reader: font/spacing/margin/themes (dark/black/white) / immersive / virtual pages / annotations + notes
- [ ] PDF reader
- [ ] Folder-based scanning + cover & metadata extraction (ComicInfo.xml + EPUB)
- [ ] Incremental scans (skip unchanged) + folder watching/auto-import
- [ ] Full-text search + filtering + smart filters
- [ ] Collections, reading lists (CBL import), series relationships, want-to-read
- [ ] Mixed-media libraries
- [ ] Ratings & reviews
- [ ] Users + roles + per-library access + age restrictions + child accounts
- [ ] OPDS feed (third-party reader support)
- [ ] Send-to-Kindle / email a file (optional SMTP)
- [ ] Server + per-user stats
- [ ] Theming (light/dark, custom EPUB fonts), accessibility, localization-ready
- [ ] **Plus (beyond Kavita): native SMB/UNC libraries with no host mount**
- [ ] **Plus: native Android app with offline downloads, shipped as APK**

---

## 14. Phased roadmap

**Phase 1 — Walking skeleton.** Monorepo + backend + SQLite + setup wizard + login (JWT) +
`IStorageProvider` with **SMB provider** + scanner + CBZ image reader in the web UI. Must run
end-to-end against a real SMB share.

**Phase 2 — Full readers + formats.** cbr/7z/raw images, epub + pdf readers, dual-page +
webtoon modes, progress sync, covers/metadata, search.

**Phase 3 — Organize + users.** Collections, reading lists (CBL), want-to-read, ratings/
reviews, roles, age restrictions, per-library access, admin/tasks/stats, OPDS.

**Phase 4 — Android app.** Server picker + login, browse, image + epub readers, progress sync,
offline downloads, signed APK.

**Phase 5 — Polish.** Themes, smart filters, folder-watch auto-import, send-to-Kindle, optional
OIDC, optional external metadata, localization, Docker multi-arch release.

---

## 15. Monorepo layout

```
mangrove/
├─ apps/
│  ├─ server/          # ASP.NET Core API, EF Core, scanner, storage providers, readers
│  │  ├─ Storage/      # IStorageProvider, LocalStorageProvider, SmbStorageProvider
│  │  ├─ Readers/      # archive/epub/pdf readers
│  │  ├─ Scanning/     # parser + scan jobs
│  │  ├─ Auth/         # identity, jwt, roles
│  │  └─ Data/         # entities, DbContext, migrations
│  ├─ web/             # React + Vite + Tailwind + shadcn/ui
│  │  └─ src/assets/   # mangrove-icon.svg, mangrove-wordmark.svg
│  └─ android/         # Kotlin + Jetpack Compose
├─ packages/
│  └─ shared-types/    # OpenAPI-generated TS client + shared DTOs
├─ docker/             # Dockerfile, compose
├─ brand/              # logos
└─ README.md
```

---

## 16. Branding assets

- `mangrove-icon.svg` — square app icon (teal gradient, white open book, mint canopy).
  Use as the **login page logo** (centered above the form) and as the source for Android
  launcher icons.
- `mangrove-wordmark.svg` — horizontal lockup (mark + “Mangrove”) for the top nav and the
  login header.
- Palette: teal `#0D9488`, deep teal `#0F3D38`, mint accent `#2DD4BF`, ink `#0B2D2A`.
- To rename the product: change the `APP_NAME` constant (server + web + android) and swap the
  two SVGs.
