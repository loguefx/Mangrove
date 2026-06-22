import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api, type CollectionDto } from "../api";
import { PageHeader } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";

export default function Collections() {
  const [collections, setCollections] = useState<CollectionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [name, setName] = useState("");
  const [isPublic, setIsPublic] = useState(false);

  const load = () => api.collections().then(setCollections).catch(() => setCollections([]));

  useEffect(() => {
    load().finally(() => setLoading(false));
  }, []);

  const create = async () => {
    if (!name.trim()) return;
    await api.createCollection(name.trim(), isPublic);
    setName("");
    setIsPublic(false);
    await load();
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <PageHeader title="Collections" />

      <div className="mb-6 flex flex-wrap items-center gap-2 rounded-2xl border border-neutral-800 bg-neutral-900/60 p-4">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="New collection name"
          className="input flex-1"
        />
        <label className="flex items-center gap-2 text-sm text-neutral-400">
          <input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />
          Public
        </label>
        <button
          onClick={create}
          className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90"
        >
          Create
        </button>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <Spinner />
        </div>
      ) : collections.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-neutral-800 p-12 text-center text-neutral-500">
          No collections yet.
        </div>
      ) : (
        <div className="space-y-2">
          {collections.map((c) => (
            <Link
              key={c.id}
              to={`/collections/${c.id}`}
              className="flex items-center justify-between rounded-2xl bg-neutral-900 px-4 py-3 ring-1 ring-white/5 transition hover:ring-teal-mint/40"
            >
              <span className="font-medium">
                {c.name}
                {c.isPublic && <span className="ml-2 text-xs text-teal-mint">Public</span>}
              </span>
              <span className="text-sm text-neutral-500">{c.itemCount} series</span>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
