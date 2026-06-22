import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api, fetchBlobObjectUrl, type ReadingListDetailDto } from "../api";
import { PageHeader } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";

export default function ReadingListDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [list, setList] = useState<ReadingListDetailDto | null>(null);
  const [loading, setLoading] = useState(true);

  const load = () => api.readingList(Number(id)).then(setList).catch(() => setList(null));

  useEffect(() => {
    if (!id) return;
    load().finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  const move = async (index: number, dir: -1 | 1) => {
    if (!list) return;
    const ids = list.items.map((i) => i.id);
    const j = index + dir;
    if (j < 0 || j >= ids.length) return;
    [ids[index], ids[j]] = [ids[j], ids[index]];
    await api.reorderReadingList(list.id, ids);
    await load();
  };

  const removeItem = async (itemId: number) => {
    if (!list) return;
    await api.removeFromReadingList(list.id, itemId);
    await load();
  };

  const remove = async () => {
    if (!list) return;
    if (!confirm(`Delete reading list "${list.name}"?`)) return;
    await api.deleteReadingList(list.id);
    navigate("/reading-lists");
  };

  const exportCbl = async () => {
    if (!list) return;
    const url = await fetchBlobObjectUrl(`/api/reading-lists/${list.id}/export-cbl`);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${list.name}.cbl`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner />
      </div>
    );
  }
  if (!list) return <div className="p-6">Reading list not found.</div>;

  return (
    <div className="mx-auto max-w-4xl p-6">
      <PageHeader title={list.name}>
        <button
          onClick={exportCbl}
          className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:border-teal-mint hover:text-teal-mint"
        >
          Export CBL
        </button>
        <button
          onClick={remove}
          className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-red-400 hover:border-red-500"
        >
          Delete
        </button>
      </PageHeader>

      {list.items.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-neutral-800 p-12 text-center text-neutral-500">
          No chapters yet. Add chapters from a series page.
        </div>
      ) : (
        <div className="divide-y divide-neutral-800 overflow-hidden rounded-2xl ring-1 ring-white/5">
          {list.items.map((item, index) => (
            <div key={item.id} className="flex items-center gap-3 bg-neutral-900 px-4 py-3">
              <span className="w-6 text-center text-sm text-neutral-600">{index + 1}</span>
              <Link to={`/reader/${item.chapterId}`} className="flex-1">
                <span className="font-medium">{item.seriesName}</span>
                <span className="ml-2 text-sm text-neutral-500">
                  {item.title ?? (item.chapterNumber > 0 ? `Ch ${item.chapterNumber}` : "Chapter")}
                </span>
              </Link>
              <button onClick={() => move(index, -1)} className="text-neutral-400 hover:text-teal-mint" title="Move up">
                ↑
              </button>
              <button onClick={() => move(index, 1)} className="text-neutral-400 hover:text-teal-mint" title="Move down">
                ↓
              </button>
              <button onClick={() => removeItem(item.id)} className="text-neutral-400 hover:text-red-400" title="Remove">
                ✕
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
