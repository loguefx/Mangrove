import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, type SearchResultDto } from "../api";
import { Spinner } from "../components/Spinner";

export default function SearchPage() {
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState(params.get("q") ?? "");
  const [results, setResults] = useState<SearchResultDto[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const term = params.get("q") ?? "";
    if (!term.trim()) {
      setResults([]);
      return;
    }
    setLoading(true);
    api
      .search(term)
      .then(setResults)
      .catch(() => setResults([]))
      .finally(() => setLoading(false));
  }, [params]);

  const submit = (e: React.FormEvent) => {
    e.preventDefault();
    setParams(q.trim() ? { q: q.trim() } : {});
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <Link to="/" className="mb-4 inline-block text-sm text-teal-mint hover:underline">
        ← Back to library
      </Link>
      <form onSubmit={submit} className="mb-6 flex gap-2">
        <input
          autoFocus
          value={q}
          onChange={(e) => setQ(e.target.value)}
          placeholder="Search all series, summaries, genres, tags…"
          className="flex-1 rounded-xl border border-neutral-700 bg-neutral-800 px-4 py-2 text-sm outline-none focus:border-teal-mint"
        />
        <button className="rounded-xl bg-teal px-5 py-2 text-sm font-medium text-white hover:bg-teal/90">
          Search
        </button>
      </form>

      {loading ? (
        <Spinner />
      ) : results.length === 0 ? (
        <p className="text-neutral-500">{params.get("q") ? "No matches." : "Type to search."}</p>
      ) : (
        <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
          {results.map((r) => (
            <Link
              key={r.id}
              to={`/series/${r.id}`}
              className="group overflow-hidden rounded-2xl bg-neutral-900 ring-1 ring-white/5 transition hover:ring-teal-mint/40"
            >
              <div className="aspect-[2/3] w-full overflow-hidden bg-neutral-800">
                {r.hasCover ? (
                  <img
                    src={`/api/series/${r.id}/cover`}
                    alt={r.name}
                    className="h-full w-full object-cover transition group-hover:scale-[1.03]"
                    loading="lazy"
                  />
                ) : (
                  <div className="flex h-full items-center justify-center text-neutral-600">No cover</div>
                )}
              </div>
              <div className="truncate p-2.5 text-sm font-medium">{r.name}</div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
