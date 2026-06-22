# Mangrove — Self-Hosted Reading Server

> *Your whole library, rooted in one place.*

Mangrove is a self-hosted server for manga, webtoons, comics, and books. It reads your files
directly from network shares (UNC / SMB) **without mounting any drives**, serves them through a
clean web UI, and (later) pairs with a native Android app. See [`Mangrove-Spec.md`](./Mangrove-Spec.md)
for the full design.

## Status — Phase 1 (Walking skeleton) ✅

Phase 1 runs end-to-end: monorepo + ASP.NET Core backend + SQLite + first-run admin setup +
JWT login + `IStorageProvider` (Local **and** real SMB via SMBLibrary) + library scanner + a
CBZ image reader in the React web UI.

## Status — Phase 2 (Full readers + formats) ✅

Phase 2 adds:

- **More formats:** comic archives `cbz/zip/cbr/rar/cb7/7z/cbt/tar/tar.gz` (SharpCompress),
  **raw image folders** as chapters, **PDF** (server-side rasterization via Docnet/PDFium), and
  **EPUB** (VersOne.Epub). A central `FormatRegistry`/`ReaderService` dispatches all of these.
- **Web image reader:** single-page, **double-page spread**, and **webtoon continuous scroll**,
  plus fit modes (screen/width/height/original) — all persisted, direction-aware (RTL/LTR).
- **Web EPUB reader:** spine rendering in a sandboxed iframe with client-side resource inlining,
  table of contents, and font-size + theme (light/sepia/dark) controls.
- **Metadata:** parses `ComicInfo.xml` and EPUB OPF metadata during scans; an admin
  **metadata editor** whose edits lock the series so future scans won't overwrite them.
- **Search** (`/api/search`) and a **dashboard** with *Continue reading* + *Recently added*.
- Reading-progress resume across all readers.

The `scripts/` folder has `make-samples.ps1` (generates a multi-format sample library) and
`e2e-phase2.ps1` (drives the full pipeline against a running server) for local verification.

## Status — Phase 3 (Organize + users) ✅

Phase 3 adds:

- **Users & roles:** admin user management (`/api/users`) — create users, assign `Admin/User/ReadOnly`
  roles, lock/unlock, reset passwords, and delete (the last admin is protected).
- **Per-library access & age restrictions:** browsing (libraries, series, search, dashboard, readers,
  OPDS) is filtered to the libraries a user is granted, and by a per-user **age-rating tier**
  (with an *include unrated* toggle). Admins see everything.
- **Collections:** owner/public collections of series with add/remove.
- **Reading lists:** ordered, chapter-level lists with reordering plus **CBL import/export**
  (ComicRack-compatible) that matches books by series name + number/volume.
- **Want-to-read**, **ratings (1–5 stars)** and **reviews** per series (aggregated on the series page).
- **Downloads** (`/api/chapters/{id}/download`) gated by the role `canDownload` flag.
- **Admin tasks & stats:** scan history (`JobLog`), *scan all libraries*, server stats and per-user
  stats, plus editable app **settings**.
- **OPDS 1.2 feed** (`/api/opds`, HTTP Basic auth) so third-party readers (KOReader, Panels, Mihon)
  can browse libraries/series and download chapters.
- **Web UI:** Admin page (users/tasks/stats/settings tabs), Collections, Reading lists (with CBL
  import/export), Want-to-read, and series-page rating/review/want-to-read/add-to-list controls.

`scripts/e2e-phase3.ps1` exercises access control, age restrictions, collections, the CBL
round-trip, ratings, stats, tasks, downloads, and the OPDS feed against a running server.

## Status — Phase 4 (Android app + deployment) 🚧

- **Native Android app** (`apps/android`, Kotlin + Jetpack Compose): server picker → login →
  Home (continue reading / recently added) → library grid → series → paged image reader with
  LTR/RTL, progress sync, and the **synced per-user reading direction**. Sessions persist via a
  persistent cookie jar + automatic token refresh. A prebuilt debug APK ships at
  [`apps/android/dist/mangrove-debug.apk`](./apps/android/dist/mangrove-debug.apk) — sideload it,
  or build your own (see [`apps/android/README.md`](./apps/android/README.md)).
- **Run the server as a Windows service** via `Mangrove.exe install` (see below).

## Monorepo layout

```
mangrove/
├─ apps/
│  ├─ server/          # ASP.NET Core 8 API, EF Core, scanner, storage providers, readers
│  │  ├─ Auth/         # JWT, password hashing, first-run admin
│  │  ├─ Data/         # entities, DbContext, migrations
│  │  ├─ Readers/      # archive/image-folder/PDF/EPUB readers, FormatRegistry, ComicInfo
│  │  ├─ Scanning/     # filename parser + library scanner
│  │  ├─ Security/     # AES-GCM credential protector
│  │  └─ Storage/      # IStorageProvider, Local + SMB providers, connection pool
│  ├─ web/             # React + TypeScript + Vite + Tailwind
│  │  └─ src/assets/   # mangrove-icon.svg, mangrove-wordmark.svg
│  └─ android/         # Kotlin + Jetpack Compose app (Phase 4); prebuilt APK in dist/
├─ packages/shared-types/  # shared DTO/client types (placeholder for Phase 2)
├─ tests/              # xUnit tests (scanner, SMB path resolver, auth flow)
├─ docker/             # Dockerfile + compose
├─ brand/              # canonical logos
└─ Mangrove-Spec.md
```

## Prerequisites

- .NET 8 SDK
- Node.js 18+ and npm

## Configuration

Copy `.env.example` to `.env` and set at least `MANGROVE_JWT_SECRET` and
`MANGROVE_DATAPROTECTION_KEY`. In development the server will generate temporary values and warn
you. Secrets are read from environment variables and are never committed.

## Run it (development)

**1. Backend** (creates `mangrove.db` on first run):

```powershell
cd apps/server
$env:MANGROVE_JWT_SECRET="<long-random-string>"
$env:MANGROVE_DATAPROTECTION_KEY="<base64-32-bytes>"
dotnet run
# API on http://localhost:5000  (Swagger UI at /swagger)
```

> If port 5000 is taken, set `$env:MANGROVE_PORT="5080"` and pass the same value to the web app
> via `$env:VITE_API_TARGET="http://localhost:5080"`.

**2. Web** (in a second terminal):

```powershell
cd apps/web
npm install
npm run dev
# Web on http://localhost:5173 (proxies /api to the backend)
```

Open http://localhost:5173 — you'll be guided through the **first-run admin setup**, then the
branded login. Add a library (Local or SMB), run a **Scan**, open a series, and read a CBZ.

## Run it as a Windows service

The server can run unattended as a Windows service so it starts on boot and keeps running with no
console window. The published executable is **`Mangrove.exe`**.

**1. Publish a self-contained-ish build:**

```powershell
dotnet publish apps/server/Mangrove.Server.csproj -c Release -o C:\Mangrove
```

**2. Set machine-wide secrets** (so the service has them — service processes don't see your user
env vars unless they're system-level):

```powershell
# Run as Administrator
[Environment]::SetEnvironmentVariable("MANGROVE_JWT_SECRET", "<long-random-string>", "Machine")
[Environment]::SetEnvironmentVariable("MANGROVE_DATAPROTECTION_KEY", "<base64-32-bytes>", "Machine")
# Optional: [Environment]::SetEnvironmentVariable("MANGROVE_PORT", "5080", "Machine")
```

**3. Install + start the service** from an **elevated** terminal:

```powershell
cd C:\Mangrove
.\Mangrove.exe install     # registers the "Mangrove" service (auto-start on boot)
.\Mangrove.exe start
```

The database and cover cache are created next to `Mangrove.exe` (here, `C:\Mangrove`), or wherever
`MANGROVE_DB_PATH` / `MANGROVE_CACHE_DIR` point.

**Other commands:**

```powershell
.\Mangrove.exe status      # show service state (no admin needed)
.\Mangrove.exe stop
.\Mangrove.exe restart
.\Mangrove.exe uninstall   # stop + remove the service
```

> `install`, `uninstall`, `start`, `stop`, and `restart` require an Administrator prompt. The
> service is configured to auto-restart if it crashes.

## Tests

```powershell
dotnet test tests/Mangrove.Server.Tests
```

Covers the library scanner, the SMB/UNC path resolver, and the auth flow.
