import { Link } from "react-router-dom";
import type { SeriesDto } from "../api";

const GRID_CLASS =
  "grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6";

export function SeriesGrid({
  series,
  empty,
  badges,
}: {
  series: SeriesDto[];
  empty?: string;
  /** Optional map of seriesId -> count, shown as a "N new" badge on the cover. */
  badges?: Record<number, number>;
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
        <SeriesCard key={s.id} series={s} badge={badges?.[s.id] ?? 0} />
      ))}
    </div>
  );
}

function SeriesCard({ series: s, badge }: { series: SeriesDto; badge: number }) {
  const total = s.chapterCount;
  const read = Math.min(s.readChapters ?? 0, total);
  const completed = total > 0 && read >= total;
  const progress = total > 0 ? read / total : 0;
  const inProgress = read > 0 && !completed;

  return (
    <Link
      to={`/series/${s.id}`}
      className="group overflow-hidden rounded-2xl bg-neutral-900 shadow-sm ring-1 ring-white/5 transition hover:-translate-y-0.5 hover:ring-teal-mint/40"
    >
      <div className="relative aspect-[2/3] w-full overflow-hidden bg-neutral-800">
        {badge > 0 && (
          <span className="absolute right-1.5 top-1.5 z-10 rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white shadow">
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
            src={`/api/series/${s.id}/cover`}
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
