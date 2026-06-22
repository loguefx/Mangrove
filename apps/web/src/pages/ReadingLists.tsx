import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { api, type ReadingListDto } from "../api";
import { PageHeader } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";

export default function ReadingLists() {
  const [lists, setLists] = useState<ReadingListDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [name, setName] = useState("");
  const [isPublic, setIsPublic] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = () => api.readingLists().then(setLists).catch(() => setLists([]));

  useEffect(() => {
    load().finally(() => setLoading(false));
  }, []);

  const create = async () => {
    if (!name.trim()) return;
    await api.createReadingList(name.trim(), isPublic);
    setName("");
    setIsPublic(false);
    await load();
  };

  const importCbl = async (file: File) => {
    setNotice(null);
    const xml = await file.text();
    try {
      const r = await api.importCbl(file.name.replace(/\.cbl$/i, ""), xml);
      setNotice(`Imported "${r.name}": ${r.matched} matched, ${r.unmatched} unmatched.`);
      await load();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "Import failed");
    }
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <PageHeader title="Reading lists">
        <button
          onClick={() => fileRef.current?.click()}
          className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:border-teal-mint hover:text-teal-mint"
        >
          Import CBL
        </button>
        <input
          ref={fileRef}
          type="file"
          accept=".cbl,.xml"
          className="hidden"
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) importCbl(f);
            e.target.value = "";
          }}
        />
      </PageHeader>

      {notice && (
        <div className="mb-4 rounded-xl bg-teal/10 px-4 py-2 text-sm text-teal-mint">{notice}</div>
      )}

      <div className="mb-6 flex flex-wrap items-center gap-2 rounded-2xl border border-neutral-800 bg-neutral-900/60 p-4">
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="New reading list name"
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
      ) : lists.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-neutral-800 p-12 text-center text-neutral-500">
          No reading lists yet.
        </div>
      ) : (
        <div className="space-y-2">
          {lists.map((l) => (
            <Link
              key={l.id}
              to={`/reading-lists/${l.id}`}
              className="flex items-center justify-between rounded-2xl bg-neutral-900 px-4 py-3 ring-1 ring-white/5 transition hover:ring-teal-mint/40"
            >
              <span className="font-medium">
                {l.name}
                {l.isPublic && <span className="ml-2 text-xs text-teal-mint">Public</span>}
              </span>
              <span className="text-sm text-neutral-500">{l.itemCount} chapters</span>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
