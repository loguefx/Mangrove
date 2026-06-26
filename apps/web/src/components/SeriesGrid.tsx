import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, type OnlineCandidate, type SeriesDto } from "../api";

const GRID_CLASS =
  "grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6";

export function SeriesGrid({
  series,
  empty,
  badges,
  isAdmin = false,
  favoriteIds,
  onChanged,
}: {
  series: SeriesDto[];
  empty?: string;
  /** Optional map of seriesId -> count, shown as a "N new" badge on the cover. */
  badges?: Record<number, number>;
  /** Show admin-only actions (Identify) in the hover menu. */
  isAdmin?: boolean;
  /** Series ids currently favorited, used to pre-fill the heart toggle. */
  favoriteIds?: Set<number>;
  /** Called after an action changes a series (favorite/identify) so the parent can refresh. */
  onChanged?: () => void;
}) {
  if (series.length === 0) {
    return (
      <div className="rounded-2xl border border-dashed border-neutral-800 p-12 text-center text-neutral-500">
        {empty ?? "Nothing here yet."}
      </div>
    );
  }
  return (
    <div className={GRID_CLASS}>
      {series.map((s) => (
        <SeriesCard
          key={s.id}
          series={s}
          badge={badges?.[s.id] ?? 0}
          isAdmin={isAdmin}
          initialFavorite={favoriteIds?.has(s.id) ?? false}
          onChanged={onChanged}
        />
      ))}
    </div>
  );
}

function SeriesCard({
  series: s,
  badge,
  isAdmin,
  initialFavorite,
  onChanged,
}: {
  series: SeriesDto;
  badge: number;
  isAdmin: boolean;
  initialFavorite: boolean;
  onChanged?: () => void;
}) {
  const total = s.chapterCount;
  const read = Math.min(s.readChapters ?? 0, total);
  const completed = total > 0 && read >= total;
  const progress = total > 0 ? read / total : 0;
  const inProgress = read > 0 && !completed;

  const [favorite, setFavorite] = useState(initialFavorite);
  const [menuOpen, setMenuOpen] = useState(false);
  const [identifyOpen, setIdentifyOpen] = useState(false);
  const [coverV, setCoverV] = useState(0);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!menuOpen) return;
    const onDoc = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [menuOpen]);

  const toggleFavorite = async (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    const next = !favorite;
    setFavorite(next);
    try {
      if (next) await api.addWantToRead(s.id);
      else await api.removeWantToRead(s.id);
      onChanged?.();
    } catch {
      setFavorite(!next); // revert on failure
    }
  };

  const stop = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
  };

  return (
    <div className="group relative">
      <Link
        to={`/series/${s.id}`}
        className="block overflow-hidden rounded-2xl bg-neutral-900 shadow-sm ring-1 ring-white/5 transition hover:-translate-y-0.5 hover:ring-teal-mint/40"
      >
        <div className="relative aspect-[2/3] w-full overflow-hidden bg-neutral-800">
          {badge > 0 && (
            <span className="absolute right-1.5 top-1.5 z-10 rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white shadow transition group-hover:opacity-0">
              {badge} new
            </span>
          )}
          {completed && (
            <span
              className="absolute left-1.5 top-1.5 z-10 flex h-6 w-6 items-center justify-center rounded-full bg-teal text-xs font-bold text-white shadow"
              title="Completed"
            >
              ✓
            </span>
          )}
          {s.hasCover ? (
            <img
              src={`/api/series/${s.id}/cover${coverV ? `?v=${coverV}` : ""}`}
              alt={s.name}
              className="h-full w-full object-cover transition duration-300 group-hover:scale-[1.04]"
              loading="lazy"
            />
          ) : (
            <div className="flex h-full items-center justify-center text-neutral-600">No cover</div>
          )}

          {/* Gradient + title overlay for a cleaner, poster-like look. */}
          <div className="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/30 to-transparent p-2.5 pt-8">
            <div className="line-clamp-2 text-sm font-semibold leading-tight text-white drop-shadow">
              {s.name}
            </div>
            <div className="mt-0.5 text-[11px] text-neutral-300">
              {completed ? "Read" : inProgress ? `${read}/${total} read` : `${total} chapter${total === 1 ? "" : "s"}`}
            </div>
          </div>

          {/* Reading-progress sliver. */}
          {inProgress && (
            <div className="absolute inset-x-0 bottom-0 h-1 bg-black/40">
              <div className="h-full bg-teal-mint" style={{ width: `${Math.round(progress * 100)}%` }} />
            </div>
          )}
        </div>
      </Link>

      {/* Hover actions (favorite + more menu), Jellyfin-style. */}
      <div className="absolute right-1.5 top-1.5 z-20 flex gap-1.5 opacity-0 transition group-hover:opacity-100 focus-within:opacity-100">
        <button
          onClick={toggleFavorite}
          title={favorite ? "Remove from favorites" : "Add to favorites"}
          aria-label={favorite ? "Remove from favorites" : "Add to favorites"}
          className="flex h-8 w-8 items-center justify-center rounded-full bg-black/60 text-sm text-white backdrop-blur transition hover:bg-black/80"
        >
          <span className={favorite ? "text-rose-400" : "text-white"}>{favorite ? "♥" : "♡"}</span>
        </button>
        <div ref={menuRef} className="relative">
          <button
            onClick={(e) => {
              stop(e);
              setMenuOpen((v) => !v);
            }}
            title="More"
            aria-label="More actions"
            className="flex h-8 w-8 items-center justify-center rounded-full bg-black/60 text-lg leading-none text-white backdrop-blur transition hover:bg-black/80"
          >
            ⋯
          </button>
          {menuOpen && (
            <div
              className="absolute right-0 top-9 z-30 w-44 overflow-hidden rounded-xl border border-neutral-700 bg-neutral-900/95 py-1 text-sm shadow-xl backdrop-blur"
              onClick={stop}
            >
              <Link
                to={`/series/${s.id}`}
                className="block px-3 py-2 text-neutral-200 hover:bg-neutral-800"
              >
                Open
              </Link>
              <button
                onClick={(e) => {
                  stop(e);
                  setMenuOpen(false);
                  void toggleFavorite(e);
                }}
                className="block w-full px-3 py-2 text-left text-neutral-200 hover:bg-neutral-800"
              >
                {favorite ? "Remove favorite" : "Add favorite"}
              </button>
              {isAdmin && (
                <button
                  onClick={(e) => {
                    stop(e);
                    setMenuOpen(false);
                    setIdentifyOpen(true);
                  }}
                  className="block w-full px-3 py-2 text-left text-neutral-200 hover:bg-neutral-800"
                >
                  Identify (metadata)…
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      {identifyOpen && (
        <IdentifyDialog
          seriesId={s.id}
          seriesName={s.name}
          onClose={() => setIdentifyOpen(false)}
          onApplied={() => {
            setIdentifyOpen(false);
            setCoverV(Date.now()); // bust the cover cache so the new art shows immediately
            onChanged?.();
          }}
        />
      )}
    </div>
  );
}

function IdentifyDialog({
  seriesId,
  seriesName,
  onClose,
  onApplied,
}: {
  seriesId: number;
  seriesName: string;
  onClose: () => void;
  onApplied: () => void;
}) {
  const [name, setName] = useState(seriesName);
  const [anilistId, setAnilistId] = useState("");
  const [results, setResults] = useState<OnlineCandidate[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [applyingId, setApplyingId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const search = async () => {
    setLoading(true);
    setError(null);
    try {
      const idNum = Number(anilistId.trim());
      const opts = anilistId.trim() && !Number.isNaN(idNum) ? { anilistId: idNum } : { name };
      const res = await api.identifySeries(seriesId, opts);
      setResults(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Search failed");
      setResults([]);
    } finally {
      setLoading(false);
    }
  };

  // Run an initial search by the current name when the dialog opens.
  useEffect(() => {
    void search();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const apply = async (c: OnlineCandidate) => {
    setApplyingId(c.aniListId);
    setError(null);
    try {
      await api.applyIdentify(seriesId, c.aniListId);
      onApplied();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not apply");
      setApplyingId(null);
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-black/70 p-4 sm:p-8"
      onClick={onClose}
    >
      <div
        className="mt-6 w-full max-w-2xl rounded-2xl border border-neutral-800 bg-neutral-900 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between border-b border-neutral-800 px-5 py-3">
          <h2 className="text-base font-semibold">Identify — find metadata</h2>
          <button onClick={onClose} className="text-neutral-400 hover:text-white" aria-label="Close">
            ✕
          </button>
        </div>

        <div className="px-5 py-4">
          <p className="mb-3 text-xs text-neutral-500">
            Search AniList by name, or enter an AniList ID directly. Pick the correct match to apply its
            summary, genres, tags, author and cover. This locks the series so scans won't overwrite it.
          </p>
          <div className="grid gap-3 sm:grid-cols-[1fr_160px]">
            <label className="text-sm">
              <span className="mb-1 block text-neutral-400">Name</span>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && search()}
                className="input w-full"
              />
            </label>
            <label className="text-sm">
              <span className="mb-1 block text-neutral-400">AniList ID</span>
              <input
                value={anilistId}
                onChange={(e) => setAnilistId(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && search()}
                placeholder="e.g. 30002"
                inputMode="numeric"
                className="input w-full"
              />
            </label>
          </div>
          <div className="mt-3">
            <button
              onClick={search}
              disabled={loading}
              className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
            >
              {loading ? "Searching…" : "Search"}
            </button>
          </div>

          {error && <p className="mt-3 text-sm text-red-400">{error}</p>}

          <div className="mt-4 max-h-[50vh] space-y-2 overflow-y-auto">
            {results && results.length === 0 && !loading && (
              <p className="py-6 text-center text-sm text-neutral-500">
                No matches. Try a different name or an AniList ID.
              </p>
            )}
            {results?.map((c) => (
              <div
                key={c.aniListId}
                className="flex gap-3 rounded-xl border border-neutral-800 bg-neutral-900/60 p-3"
              >
                <div className="h-28 w-20 shrink-0 overflow-hidden rounded-lg bg-neutral-800">
                  {c.coverUrl ? (
                    <img src={c.coverUrl} alt={c.title} className="h-full w-full object-cover" />
                  ) : (
                    <div className="flex h-full items-center justify-center text-[10px] text-neutral-600">
                      No cover
                    </div>
                  )}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-start justify-between gap-2">
                    <div className="font-medium leading-tight">{c.title}</div>
                    <button
                      onClick={() => apply(c)}
                      disabled={applyingId !== null}
                      className="shrink-0 rounded-lg bg-teal px-3 py-1 text-xs font-medium text-white hover:bg-teal/90 disabled:opacity-50"
                    >
                      {applyingId === c.aniListId ? "Applying…" : "Select"}
                    </button>
                  </div>
                  <div className="mt-0.5 text-xs text-neutral-500">
                    {[c.format, c.year].filter(Boolean).join(" · ")} · AniList #{c.aniListId}
                  </div>
                  {c.description && (
                    <p className="mt-1 line-clamp-3 text-xs text-neutral-400">{c.description}</p>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

/** Placeholder grid shown while a library's series load. */
export function SeriesGridSkeleton({ count = 12 }: { count?: number }) {
  return (
    <div className={GRID_CLASS}>
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="overflow-hidden rounded-2xl bg-neutral-900 ring-1 ring-white/5">
          <div className="aspect-[2/3] w-full animate-pulse bg-neutral-800" />
        </div>
      ))}
    </div>
  );
}

export function PageHeader({ title, children }: { title: string; children?: React.ReactNode }) {
  return (
    <header className="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div className="flex items-center gap-3">
        <Link to="/" className="text-sm text-teal-mint hover:underline">
          ← Home
        </Link>
        <h1 className="text-xl font-semibold">{title}</h1>
      </div>
      <div className="flex items-center gap-2">{children}</div>
    </header>
  );
}
