import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, type CollectionDetailDto } from "../api";
import { PageHeader, SeriesGrid } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";

export default function CollectionDetail() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [collection, setCollection] = useState<CollectionDetailDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!id) return;
    api.collection(Number(id)).then(setCollection).catch(() => setCollection(null)).finally(() => setLoading(false));
  }, [id]);

  const remove = async () => {
    if (!collection) return;
    if (!confirm(`Delete collection "${collection.name}"?`)) return;
    await api.deleteCollection(collection.id);
    navigate("/collections");
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner />
      </div>
    );
  }
  if (!collection) return <div className="p-6">Collection not found.</div>;

  return (
    <div className="mx-auto max-w-6xl p-6">
      <PageHeader title={collection.name}>
        <button
          onClick={remove}
          className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-red-400 hover:border-red-500"
        >
          Delete
        </button>
      </PageHeader>
      <SeriesGrid series={collection.series} empty="No series in this collection yet." />
    </div>
  );
}
