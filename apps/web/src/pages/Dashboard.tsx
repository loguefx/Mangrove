import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, type DashboardDto, type FavoriteUnread, type LibraryDto, type SeriesDto } from "../api";
import { useAuth } from "../auth";
import { MangroveWordmark } from "../components/Brand";
import { Spinner } from "../components/Spinner";
import { AddLibraryDialog } from "../components/AddLibraryDialog";
import { SeriesGrid } from "../components/SeriesGrid";

type View = "home" | number;

export default function Dashboard() {
  const { user, logout } = useAuth();
  const isAdmin = user?.roles.includes("Admin") ?? false;

  const [libraries, setLibraries] = useState<LibraryDto[]>([]);
  // The selected library lives in the URL (?lib=ID) so the browser back button returns to it.
  const [searchParams, setSearchParams] = useSearchParams();
  const libParam = searchParams.get("lib");
  const libNum = libParam ? Number(libParam) : NaN;
  const view: View = !Number.isNaN(libNum) ? libNum : "home";
  const setView = (v: View) => {
    if (v === "home") setSearchParams({});
    else setSearchParams({ lib: String(v) });
  };
  const [series, setSeries] = useState<SeriesDto[]>([]);
  const [dashboard, setDashboard] = useState<DashboardDto | null>(null);
  const [wantToRead, setWantToRead] = useState<SeriesDto[]>([]);
  const [catchUp, setCatchUp] = useState<FavoriteUnread[]>([]);
  const [loading, setLoading] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [showAdd, setShowAdd] = useState(false);
  const [search, setSearch] = useState("");
  const [notice, setNotice] = useState<string | null>(null);

  const loadLibraries = async () => {
    const libs = await api.libraries();
    setLibraries(libs);
    return libs;
  };

  const loadHome = async () => {
    api.dashboard().then(setDashboard).catch(() => setDashboard(null));
    api.wantToRead().then(setWantToRead).catch(() => setWantToRead([]));
    api.favoritesUnread().then(setCatchUp).catch(() => setCatchUp([]));
  };

  useEffect(() => {
    Promise.all([loadLibraries(), loadHome()]).finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const viewRef = useRef<View>(view);
  useEffect(() => {
    viewRef.current = view;
  }, [view]);

  const searchRef = useRef(search);
  useEffect(() => {
    searchRef.current = search;
  }, [search]);

  // Auto-refresh: periodically pull fresh content so newly-scanned chapters/series appear without a
  // manual refresh. Pauses while the tab is hidden or a manual scan is already polling.
  useEffect(() => {
    const REFRESH_MS = 45000;
    const id = setInterval(() => {
      if (document.hidden || scanning) return;
      loadLibraries();
      loadHome();
      const v = viewRef.current;
      if (typeof v === "number") {
        api.series(v, searchRef.current || undefined).then(setSeries).catch(() => {});
      }
    }, REFRESH_MS);
    return () => clearInterval(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scanning]);

  // Persist the window scroll position per-library so we can restore it on return.
  const scrollKey = (lib: number) => `mg-scroll-lib-${lib}`;
  const pendingRestore = useRef<number | null>(null);

  useEffect(() => {
    if (typeof view !== "number") return;
    const lib = view;
    const saveScroll = () => sessionStorage.setItem(scrollKey(lib), String(window.scrollY));
    window.addEventListener("scroll", saveScroll, { passive: true });
    return () => window.removeEventListener("scroll", saveScroll);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [view]);

  // On arriving at a library view, queue a one-time scroll restore (after series render).
  useEffect(() => {
    if (typeof view === "number") {
      pendingRestore.current = view;
      const saved = Number(sessionStorage.getItem(scrollKey(view)) || 0);
      if (saved <= 0) window.scrollTo(0, 0);
    } else {
      pendingRestore.current = null;
      window.scrollTo(0, 0);
    }
  }, [view]);

  useEffect(() => {
    if (view === "home") {
      setSeries([]);
      return;
    }
    const lib = view;
    let cancelled = false;
    api
      .series(lib, search || undefined)
      .then((s) => {
        if (cancelled) return;
        setSeries(s);
        // Restore scroll only on the initial arrival to this library, not on filter/scan refreshes.
        if (pendingRestore.current === lib) {
          pendingRestore.current = null;
          const saved = Number(sessionStorage.getItem(scrollKey(lib)) || 0);
          if (saved > 0)
            requestAnimationFrame(() => requestAnimationFrame(() => window.scrollTo(0, saved)));
        }
      })
      .catch(() => {
        if (!cancelled) setSeries([]);
      });
    return () => {
      cancelled = true;
    };
  }, [view, search]);

  const scan = async (libraryId: number) => {
    setScanning(true);
    setNotice("Scan started in the background — series will appear here as they're found.");
    try {
      await api.scanLibrary(libraryId);
      // Poll the background worker, refreshing libraries/series as content is discovered.
      for (;;) {
        await new Promise((r) => setTimeout(r, 3000));
        const status = await api.scanStatus(libraryId).catch(() => null);
        await loadLibraries();
        if (viewRef.current === libraryId) {
          const s = await api.series(libraryId, search || undefined).catch(() => []);
          setSeries(s);
        }
        if (!status || status.state === "idle") break;
      }
      await loadHome();
      setNotice("Scan complete.");
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "Scan failed");
    } finally {
      setScanning(false);
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center bg-gradient-to-b from-neutral-950 to-teal-deep">
        <Spinner />
      </div>
    );
  }

  const activeLib = typeof view === "number" ? libraries.find((l) => l.id === view) : null;
  const totalNew = catchUp.reduce((n, c) => n + c.newChapters, 0);

  return (
    <div className="flex min-h-full bg-gradient-to-b from-neutral-950 to-teal-deep bg-fixed">
      {/* Left rail */}
      <aside className="hidden w-64 shrink-0 flex-col border-r border-neutral-800 bg-neutral-950/60 p-4 backdrop-blur md:flex">
        <MangroveWordmark className="mb-6 h-9 w-auto" />

        <button
          onClick={() => setView("home")}
          className={`mb-4 flex w-full items-center gap-2 rounded-xl px-3 py-2 text-left text-sm transition ${
            view === "home" ? "bg-teal/20 text-teal-mint" : "text-neutral-300 hover:bg-neutral-800"
          }`}
        >
          Home
        </button>

        <div className="mb-2 flex items-center justify-between">
          <span className="text-xs font-semibold uppercase tracking-wide text-neutral-500">
            Libraries
          </span>
          {isAdmin && (
            <button
              onClick={() => setShowAdd(true)}
              className="rounded-lg px-2 py-0.5 text-sm text-teal-mint hover:bg-neutral-800"
            >
              + Add
            </button>
          )}
        </div>
        <nav className="space-y-1">
          {libraries.map((lib) => (
            <button
              key={lib.id}
              onClick={() => setView(lib.id)}
              className={`flex w-full items-center justify-between rounded-xl px-3 py-2 text-left text-sm transition ${
                view === lib.id
                  ? "bg-teal/20 text-teal-mint"
                  : "text-neutral-300 hover:bg-neutral-800"
              }`}
            >
              <span className="truncate">{lib.name}</span>
              <span className="ml-2 rounded-full bg-neutral-800 px-2 text-xs text-neutral-400">
                {lib.seriesCount}
              </span>
            </button>
          ))}
          {libraries.length === 0 && (
            <p className="px-1 text-sm text-neutral-500">No libraries yet.</p>
          )}
        </nav>

        <div className="mb-2 mt-6 text-xs font-semibold uppercase tracking-wide text-neutral-500">
          Browse
        </div>
        <nav className="space-y-1">
          <Link
            to="/favorites"
            className="flex items-center justify-between rounded-xl px-3 py-2 text-sm text-neutral-300 hover:bg-neutral-800"
          >
            <span>Favorites</span>
            {totalNew > 0 && (
              <span className="ml-2 rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white">
                {totalNew}
              </span>
            )}
          </Link>
          <Link to="/collections" className="block rounded-xl px-3 py-2 text-sm text-neutral-300 hover:bg-neutral-800">
            Collections
          </Link>
          <Link to="/reading-lists" className="block rounded-xl px-3 py-2 text-sm text-neutral-300 hover:bg-neutral-800">
            Reading lists
          </Link>
          {isAdmin && (
            <Link to="/admin" className="block rounded-xl px-3 py-2 text-sm text-neutral-300 hover:bg-neutral-800">
              Admin
            </Link>
          )}
        </nav>

        <div className="mt-auto pt-4 text-sm text-neutral-400">
          <div className="mb-2 truncate">Signed in as {user?.username}</div>
          <button onClick={logout} className="text-neutral-400 hover:text-neutral-200">
            Sign out
          </button>
        </div>
      </aside>

      {/* Content */}
      <main className="flex-1 p-6">
        <header className="mb-6 flex flex-wrap items-center justify-between gap-3">
          <h1 className="text-xl font-semibold">{view === "home" ? "Home" : activeLib?.name ?? "Library"}</h1>
          <div className="flex items-center gap-2">
            {view !== "home" && (
              <input
                placeholder="Filter series…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="rounded-xl border border-neutral-700 bg-neutral-800 px-3 py-1.5 text-sm outline-none focus:border-teal-mint"
              />
            )}
            <Link
              to="/search"
              className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:border-teal-mint hover:text-teal-mint"
            >
              Search all
            </Link>
            {isAdmin && view !== "home" && (
              <button
                onClick={() => scan(view)}
                disabled={scanning}
                className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
              >
                {scanning ? "Scanning…" : "Scan"}
              </button>
            )}
          </div>
        </header>

        {notice && (
          <div className="mb-4 rounded-xl bg-teal/10 px-4 py-2 text-sm text-teal-mint">{notice}</div>
        )}

        {view === "home" ? (
          <HomeView
            libraries={libraries}
            dashboard={dashboard}
            wantToRead={wantToRead}
            catchUp={catchUp}
            isAdmin={isAdmin}
            onOpenLibrary={setView}
            onAddLibrary={() => setShowAdd(true)}
          />
        ) : series.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-neutral-800 p-12 text-center text-neutral-500">
            {scanning
              ? "Scanning…"
              : `No series found. ${isAdmin ? "Run a scan to populate this library." : ""}`}
          </div>
        ) : (
          <SeriesGrid series={series} />
        )}
      </main>

      {showAdd && (
        <AddLibraryDialog
          onClose={() => setShowAdd(false)}
          onCreated={async (id) => {
            setShowAdd(false);
            await loadLibraries();
            setView(id);
            // Newly added libraries start a scan automatically (matches user expectation).
            await scan(id);
          }}
        />
      )}
    </div>
  );
}

function Rail({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mb-8">
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-neutral-500">{title}</h2>
      <div className="flex gap-4 overflow-x-auto pb-2">{children}</div>
    </section>
  );
}

function CoverCard({ to, title, subtitle, src }: { to: string; title: string; subtitle?: string; src: string | null }) {
  return (
    <Link to={to} className="group w-32 shrink-0">
      <div className="aspect-[2/3] w-32 overflow-hidden rounded-xl bg-neutral-800 ring-1 ring-white/5 transition group-hover:ring-teal-mint/40">
        {src ? (
          <img src={src} alt={title} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-full items-center justify-center text-xs text-neutral-600">No cover</div>
        )}
      </div>
      <div className="mt-1.5 truncate text-xs font-medium">{title}</div>
      {subtitle && <div className="truncate text-xs text-neutral-500">{subtitle}</div>}
    </Link>
  );
}

function HomeView({
  libraries,
  dashboard,
  wantToRead,
  catchUp,
  isAdmin,
  onOpenLibrary,
  onAddLibrary,
}: {
  libraries: LibraryDto[];
  dashboard: DashboardDto | null;
  wantToRead: SeriesDto[];
  catchUp: FavoriteUnread[];
  isAdmin: boolean;
  onOpenLibrary: (id: number) => void;
  onAddLibrary: () => void;
}) {
  const empty =
    !dashboard ||
    (dashboard.continueReading.length === 0 &&
      dashboard.recentlyAdded.length === 0 &&
      catchUp.length === 0 &&
      wantToRead.length === 0);

  // Map favorited series -> its unread-new-chapter info, so we can badge covers in the Favorites rail.
  const newBySeries = new Map(catchUp.map((c) => [c.seriesId, c]));

  return (
    <div>
      {/* Catch up — new chapters in favorited series */}
      {catchUp.length > 0 && (
        <Rail title="Catch up — new in favorites">
          {catchUp.map((c) => (
            <div key={c.seriesId} className="relative w-32 shrink-0">
              <span className="absolute right-1.5 top-1.5 z-10 rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white shadow">
                {c.newChapters} new
              </span>
              <CoverCard
                to={`/reader/${c.nextChapterId}`}
                title={c.seriesName}
                subtitle={`Next: ch ${c.nextChapterNumber}`}
                src={c.hasCover ? `/api/series/${c.seriesId}/cover` : null}
              />
            </div>
          ))}
        </Rail>
      )}

      {/* Library shortcuts */}
      <section className="mb-8">
        <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-neutral-500">Libraries</h2>
        {libraries.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-neutral-800 p-8 text-center text-neutral-500">
            No libraries yet. {isAdmin && (
              <button onClick={onAddLibrary} className="text-teal-mint hover:underline">
                Add one
              </button>
            )}
          </div>
        ) : (
          <div className="flex flex-wrap gap-3">
            {libraries.map((lib) => (
              <button
                key={lib.id}
                onClick={() => onOpenLibrary(lib.id)}
                className="rounded-2xl bg-neutral-900 px-5 py-4 text-left ring-1 ring-white/5 transition hover:ring-teal-mint/40"
              >
                <div className="font-medium">{lib.name}</div>
                <div className="text-xs text-neutral-500">{lib.seriesCount} series</div>
              </button>
            ))}
          </div>
        )}
      </section>

      {dashboard && dashboard.continueReading.length > 0 && (
        <Rail title="Continue reading">
          {dashboard.continueReading.map((c) => (
            <div key={c.chapterId} className="w-32 shrink-0">
              <CoverCard
                to={`/reader/${c.chapterId}`}
                title={c.seriesName}
                subtitle={c.pageCount > 0 ? `${c.page + 1}/${c.pageCount}` : "In progress"}
                src={c.hasCover ? `/api/chapters/${c.chapterId}/cover` : null}
              />
            </div>
          ))}
        </Rail>
      )}

      {wantToRead.length > 0 && (
        <Rail title="Favorites">
          {wantToRead.map((s) => {
            const unread = newBySeries.get(s.id);
            return (
              <div key={s.id} className="relative w-32 shrink-0">
                {unread && (
                  <span
                    className="absolute right-1.5 top-1.5 z-10 rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white shadow"
                    title={`${unread.newChapters} new chapter${unread.newChapters > 1 ? "s" : ""} to read`}
                  >
                    {unread.newChapters} new
                  </span>
                )}
                <CoverCard
                  to={`/series/${s.id}`}
                  title={s.name}
                  src={s.hasCover ? `/api/series/${s.id}/cover` : null}
                />
              </div>
            );
          })}
        </Rail>
      )}

      {dashboard && dashboard.recentlyAdded.length > 0 && (
        <Rail title="Recently added">
          {dashboard.recentlyAdded.map((s) => (
            <CoverCard
              key={s.id}
              to={`/series/${s.id}`}
              title={s.name}
              src={s.hasCover ? `/api/series/${s.id}/cover` : null}
            />
          ))}
        </Rail>
      )}

      {empty && libraries.length > 0 && (
        <div className="rounded-2xl border border-dashed border-neutral-800 p-12 text-center text-neutral-500">
          Nothing to show yet. Open a library to start reading.
        </div>
      )}
    </div>
  );
}
