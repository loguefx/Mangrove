import { Link } from "react-router-dom";
import type { SeriesDto } from "../api";

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
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
      {series.map((s) => (
        <Link
          key={s.id}
          to={`/series/${s.id}`}
          className="group overflow-hidden rounded-2xl bg-neutral-900 shadow-sm ring-1 ring-white/5 transition hover:ring-teal-mint/40"
        >
          <div className="relative aspect-[2/3] w-full overflow-hidden bg-neutral-800">
            {badges && badges[s.id] > 0 && (
              <span className="absolute right-1.5 top-1.5 z-10 rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white shadow">
                {badges[s.id]} new
              </span>
            )}
            {s.hasCover ? (
              <img
                src={`/api/series/${s.id}/cover`}
                alt={s.name}
                className="h-full w-full object-cover transition group-hover:scale-[1.03]"
                loading="lazy"
              />
            ) : (
              <div className="flex h-full items-center justify-center text-neutral-600">No cover</div>
            )}
          </div>
          <div className="p-2.5">
            <div className="truncate text-sm font-medium">{s.name}</div>
            <div className="text-xs text-neutral-500">{s.chapterCount} chapter(s)</div>
          </div>
        </Link>
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
