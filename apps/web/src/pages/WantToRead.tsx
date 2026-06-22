import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, type FavoriteUnread, type SeriesDto } from "../api";
import { PageHeader, SeriesGrid } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";

export default function WantToRead() {
  const [series, setSeries] = useState<SeriesDto[]>([]);
  const [unread, setUnread] = useState<FavoriteUnread[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      api.wantToRead().then(setSeries).catch(() => setSeries([])),
      api.favoritesUnread().then(setUnread).catch(() => setUnread([])),
    ]).finally(() => setLoading(false));
  }, []);

  const badges = Object.fromEntries(unread.map((u) => [u.seriesId, u.newChapters]));
  const totalNew = unread.reduce((n, u) => n + u.newChapters, 0);

  return (
    <div className="mx-auto max-w-6xl p-6">
      <PageHeader title="Favorites" />

      {!loading && totalNew > 0 && (
        <div className="mb-5 flex flex-wrap items-center gap-2 rounded-xl bg-rose-500/10 px-4 py-3 text-sm text-rose-200">
          <span className="rounded-full bg-rose-500 px-2 py-0.5 text-xs font-semibold text-white">
            {totalNew} new
          </span>
          <span>
            You have {totalNew} new chapter{totalNew === 1 ? "" : "s"} across {unread.length} favorite
            {unread.length === 1 ? "" : "s"} to catch up on.
          </span>
          {unread[0] && (
            <Link
              to={`/reader/${unread[0].nextChapterId}`}
              className="ml-auto rounded-lg bg-rose-500 px-3 py-1 text-xs font-medium text-white hover:bg-rose-500/90"
            >
              Start reading
            </Link>
          )}
        </div>
      )}

      {loading ? (
        <div className="flex justify-center py-12">
          <Spinner />
        </div>
      ) : (
        <SeriesGrid
          series={series}
          badges={badges}
          empty="No favorites yet. Open a series and tap ★ Favorite to follow it."
        />
      )}
    </div>
  );
}
