import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  api,
  fetchBlobObjectUrl,
  type CollectionDto,
  type ReadingListDto,
  type ReviewDto,
  type SeriesDetailDto,
} from "../api";
import { useAuth } from "../auth";
import { Spinner } from "../components/Spinner";

export default function SeriesPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const isAdmin = user?.roles.includes("Admin") ?? false;
  const canDownload = isAdmin || (user ? !user.roles.includes("ReadOnly") : false);
  const [series, setSeries] = useState<SeriesDetailDto | null>(null);
  const [reviews, setReviews] = useState<ReviewDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(false);
  const [coverVersion, setCoverVersion] = useState(0);

  const reload = async () => {
    if (!id) return;
    const detail = await api.seriesDetail(Number(id));
    setSeries(detail);
    api.reviews(Number(id)).then(setReviews).catch(() => setReviews([]));
  };

  useEffect(() => {
    reload().finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  // Auto-refresh so newly-scanned chapters appear while the page is open. Skipped while editing
  // metadata or when the tab is hidden.
  useEffect(() => {
    if (!id || editing) return;
    const iv = setInterval(() => {
      if (document.hidden) return;
      api.seriesDetail(Number(id)).then(setSeries).catch(() => {});
    }, 45000);
    return () => clearInterval(iv);
  }, [id, editing]);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner />
      </div>
    );
  }
  if (!series) return <div className="p-6">Series not found.</div>;

  const toggleWant = async () => {
    if (series.wantToRead) await api.removeWantToRead(series.id);
    else await api.addWantToRead(series.id);
    setSeries({ ...series, wantToRead: !series.wantToRead });
  };

  const rate = async (stars: number) => {
    await api.rate(series.id, stars);
    await reload();
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <button
        onClick={() => navigate(-1)}
        className="mb-4 inline-block text-sm text-teal-mint hover:underline"
      >
        ← Back
      </button>

      <div className="mb-8 flex gap-6">
        <div className="h-64 w-44 shrink-0 overflow-hidden rounded-2xl bg-neutral-800 ring-1 ring-white/5">
          {series.hasCover ? (
            <img
              src={`/api/series/${series.id}/cover${coverVersion ? `?v=${coverVersion}` : ""}`}
              alt={series.name}
              className="h-full w-full object-cover"
            />
          ) : (
            <div className="flex h-full items-center justify-center text-neutral-600">No cover</div>
          )}
        </div>
        <div className="flex-1">
          <div className="flex items-start justify-between gap-3">
            <h1 className="text-2xl font-semibold">{series.name}</h1>
            {isAdmin && !editing && (
              <button
                onClick={() => setEditing(true)}
                className="shrink-0 rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:border-teal-mint hover:text-teal-mint"
              >
                Edit metadata
              </button>
            )}
          </div>

          <div className="mt-3 flex flex-wrap items-center gap-3">
            <StarRating value={series.myStars ?? 0} onChange={rate} />
            {series.averageRating != null && (
              <span className="text-sm text-neutral-400">
                Avg {series.averageRating.toFixed(1)} ({series.ratingCount})
              </span>
            )}
            <button
              onClick={toggleWant}
              className={`rounded-xl px-3 py-1.5 text-sm transition ${
                series.wantToRead
                  ? "bg-teal/20 text-teal-mint"
                  : "border border-neutral-700 text-neutral-300 hover:border-teal-mint hover:text-teal-mint"
              }`}
            >
              {series.wantToRead ? "★ Favorited" : "☆ Favorite"}
            </button>
            <AddToCollection seriesId={series.id} />
          </div>

          {(series.genres || series.tags || series.publisher || series.ageRating || series.language) && (
            <div className="mt-3 flex flex-wrap gap-2 text-xs">
              {series.ageRating && (
                <span className="rounded-full bg-neutral-800 px-2 py-0.5 text-neutral-300">
                  {series.ageRating}
                </span>
              )}
              {series.language && (
                <span className="rounded-full bg-neutral-800 px-2 py-0.5 uppercase text-neutral-300">
                  {series.language}
                </span>
              )}
              {series.publisher && (
                <span className="rounded-full bg-neutral-800 px-2 py-0.5 text-neutral-300">
                  {series.publisher}
                </span>
              )}
              {series.genres?.split(",").filter(Boolean).map((g) => (
                <span key={`g-${g}`} className="rounded-full bg-neutral-800 px-2 py-0.5 text-neutral-400">
                  {g.trim()}
                </span>
              ))}
              {series.tags?.split(",").filter(Boolean).map((t) => (
                <span key={`t-${t}`} className="rounded-full bg-teal/15 px-2 py-0.5 text-teal-mint">
                  {t.trim()}
                </span>
              ))}
            </div>
          )}

          {(series.writer || series.penciller) && (
            <p className="mt-2 text-sm text-neutral-500">
              {series.writer && (
                <>
                  Story <span className="text-neutral-300">{series.writer}</span>
                </>
              )}
              {series.writer && series.penciller && <span className="px-1.5">·</span>}
              {series.penciller && (
                <>
                  Art <span className="text-neutral-300">{series.penciller}</span>
                </>
              )}
            </p>
          )}

          {series.summary && <p className="mt-3 max-w-2xl text-neutral-400">{series.summary}</p>}
        </div>
      </div>

      {editing && (
        <MetadataEditor
          series={series}
          onCancel={() => setEditing(false)}
          onSaved={(updated) => {
            setSeries(updated);
            setEditing(false);
          }}
          onCoverUpdated={(updated) => {
            setSeries(updated);
            setCoverVersion(Date.now());
          }}
        />
      )}

      <div className="space-y-6">
        {series.volumes.map((vol) => (
          <section key={vol.id}>
            <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-neutral-500">
              {vol.number > 0 ? `Volume ${vol.number}` : "Chapters"}
            </h2>
            <div className="divide-y divide-neutral-800 overflow-hidden rounded-2xl ring-1 ring-white/5">
              {vol.chapters.map((ch) => (
                <div
                  key={ch.id}
                  className="flex items-center justify-between bg-neutral-900 px-4 py-3 transition hover:bg-neutral-800"
                >
                  <Link to={`/reader/${ch.id}`} className="flex-1">
                    <span className="font-medium">
                      {ch.title ?? (ch.number > 0 ? `Chapter ${ch.number}` : "Chapter")}
                    </span>
                    <span className="ml-2 text-sm text-neutral-500">
                      {ch.pageCount} {ch.fileFormat === "epub" ? "sections" : "pages"} ·{" "}
                      {ch.fileFormat.toUpperCase()}
                    </span>
                  </Link>
                  <div className="flex items-center gap-3">
                    <AddToReadingList chapterId={ch.id} />
                    {canDownload && ch.fileFormat !== "images" && (
                      <button
                        onClick={() => downloadChapter(ch.id)}
                        className="text-sm text-neutral-400 hover:text-teal-mint"
                        title="Download"
                      >
                        ↓
                      </button>
                    )}
                    <Link to={`/reader/${ch.id}`} className="text-sm text-teal-mint">
                      Read →
                    </Link>
                  </div>
                </div>
              ))}
            </div>
          </section>
        ))}
      </div>

      <ReviewsSection seriesId={series.id} reviews={reviews} onPosted={reload} />
    </div>
  );
}

async function downloadChapter(chapterId: number) {
  try {
    const url = await fetchBlobObjectUrl(`/api/chapters/${chapterId}/download`);
    const a = document.createElement("a");
    a.href = url;
    a.download = `chapter-${chapterId}`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  } catch {
    /* ignore */
  }
}

function StarRating({ value, onChange }: { value: number; onChange: (stars: number) => void }) {
  const [hover, setHover] = useState(0);
  return (
    <div className="flex items-center" onMouseLeave={() => setHover(0)}>
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          onMouseEnter={() => setHover(n)}
          onClick={() => onChange(n)}
          className={`text-xl leading-none ${
            n <= (hover || value) ? "text-amber-400" : "text-neutral-600"
          }`}
          title={`${n} star${n > 1 ? "s" : ""}`}
        >
          ★
        </button>
      ))}
    </div>
  );
}

function AddToCollection({ seriesId }: { seriesId: number }) {
  const [open, setOpen] = useState(false);
  const [collections, setCollections] = useState<CollectionDto[]>([]);
  const [msg, setMsg] = useState<string | null>(null);

  const openMenu = async () => {
    setOpen((v) => !v);
    if (collections.length === 0) setCollections(await api.collections().catch(() => []));
  };

  const add = async (cid: number, name: string) => {
    await api.addToCollection(cid, seriesId);
    setMsg(`Added to ${name}`);
    setOpen(false);
    setTimeout(() => setMsg(null), 2000);
  };

  return (
    <div className="relative">
      <button
        onClick={openMenu}
        className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:border-teal-mint hover:text-teal-mint"
      >
        + Collection
      </button>
      {open && (
        <div className="absolute z-10 mt-1 max-h-64 w-56 overflow-y-auto rounded-xl border border-neutral-700 bg-neutral-900 p-1 shadow-xl">
          {collections.length === 0 ? (
            <div className="px-3 py-2 text-sm text-neutral-500">No collections.</div>
          ) : (
            collections.map((c) => (
              <button
                key={c.id}
                onClick={() => add(c.id, c.name)}
                className="block w-full rounded-lg px-3 py-1.5 text-left text-sm text-neutral-300 hover:bg-neutral-800"
              >
                {c.name}
              </button>
            ))
          )}
        </div>
      )}
      {msg && <span className="ml-2 text-xs text-teal-mint">{msg}</span>}
    </div>
  );
}

function AddToReadingList({ chapterId }: { chapterId: number }) {
  const [open, setOpen] = useState(false);
  const [lists, setLists] = useState<ReadingListDto[]>([]);
  const [msg, setMsg] = useState<string | null>(null);

  const openMenu = async () => {
    setOpen((v) => !v);
    if (lists.length === 0) setLists(await api.readingLists().catch(() => []));
  };

  const add = async (lid: number) => {
    await api.addToReadingList(lid, chapterId);
    setMsg("Added");
    setOpen(false);
    setTimeout(() => setMsg(null), 1500);
  };

  return (
    <div className="relative">
      <button
        onClick={openMenu}
        className="text-sm text-neutral-400 hover:text-teal-mint"
        title="Add to reading list"
      >
        ＋
      </button>
      {open && (
        <div className="absolute right-0 z-10 mt-1 max-h-64 w-56 overflow-y-auto rounded-xl border border-neutral-700 bg-neutral-900 p-1 shadow-xl">
          {lists.length === 0 ? (
            <div className="px-3 py-2 text-sm text-neutral-500">No reading lists.</div>
          ) : (
            lists.map((l) => (
              <button
                key={l.id}
                onClick={() => add(l.id)}
                className="block w-full rounded-lg px-3 py-1.5 text-left text-sm text-neutral-300 hover:bg-neutral-800"
              >
                {l.name}
              </button>
            ))
          )}
        </div>
      )}
      {msg && <span className="ml-1 text-xs text-teal-mint">{msg}</span>}
    </div>
  );
}

function ReviewsSection({
  seriesId,
  reviews,
  onPosted,
}: {
  seriesId: number;
  reviews: ReviewDto[];
  onPosted: () => void;
}) {
  const [body, setBody] = useState("");
  const [saving, setSaving] = useState(false);

  const post = async () => {
    if (!body.trim()) return;
    setSaving(true);
    try {
      await api.review(seriesId, body.trim());
      setBody("");
      onPosted();
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="mt-10">
      <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-neutral-500">Reviews</h2>
      <div className="mb-4 rounded-2xl border border-neutral-800 bg-neutral-900/60 p-4">
        <textarea
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={3}
          placeholder="Share your thoughts…"
          className="input w-full resize-y"
        />
        <button
          onClick={post}
          disabled={saving || !body.trim()}
          className="mt-2 rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
        >
          {saving ? "Posting…" : "Post review"}
        </button>
      </div>
      <div className="space-y-3">
        {reviews.filter((r) => r.body).length === 0 ? (
          <p className="text-sm text-neutral-500">No reviews yet.</p>
        ) : (
          reviews
            .filter((r) => r.body)
            .map((r) => (
              <div key={r.userId} className="rounded-2xl border border-neutral-800 bg-neutral-900/40 p-4">
                <div className="mb-1 flex items-center gap-2">
                  <span className="text-sm font-medium">{r.username}</span>
                  {r.stars > 0 && <span className="text-sm text-amber-400">{"★".repeat(r.stars)}</span>}
                </div>
                <p className="text-sm text-neutral-300">{r.body}</p>
              </div>
            ))
        )}
      </div>
    </section>
  );
}

function MetadataEditor({
  series,
  onCancel,
  onSaved,
  onCoverUpdated,
}: {
  series: SeriesDetailDto;
  onCancel: () => void;
  onSaved: (updated: SeriesDetailDto) => void;
  onCoverUpdated: (updated: SeriesDetailDto) => void;
}) {
  const [name, setName] = useState(series.name);
  const [summary, setSummary] = useState(series.summary ?? "");
  const [genres, setGenres] = useState(series.genres ?? "");
  const [tags, setTags] = useState(series.tags ?? "");
  const [publisher, setPublisher] = useState(series.publisher ?? "");
  const [language, setLanguage] = useState(series.language ?? "");
  const [ageRating, setAgeRating] = useState(series.ageRating ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [coverFile, setCoverFile] = useState<File | null>(null);
  const [coverPreview, setCoverPreview] = useState<string | null>(null);
  const [uploadingCover, setUploadingCover] = useState(false);
  const [coverMsg, setCoverMsg] = useState<string | null>(null);

  const pickCover = (file: File | null) => {
    setCoverMsg(null);
    setCoverFile(file);
    setCoverPreview((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return file ? URL.createObjectURL(file) : null;
    });
  };

  const uploadCover = async () => {
    if (!coverFile) return;
    setUploadingCover(true);
    setCoverMsg(null);
    setError(null);
    try {
      const updated = await api.uploadSeriesCover(series.id, coverFile);
      pickCover(null);
      setCoverMsg("Cover updated.");
      onCoverUpdated(updated);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Cover upload failed");
    } finally {
      setUploadingCover(false);
    }
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      const updated = await api.updateSeries(series.id, {
        name,
        summary: summary || null,
        genres: genres || null,
        tags: tags || null,
        publisher: publisher || null,
        language: language || null,
        ageRating: ageRating || null,
      });
      onSaved(updated);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="mb-8 rounded-2xl border border-neutral-800 bg-neutral-900/60 p-5">
      <h3 className="mb-3 text-sm font-semibold uppercase tracking-wide text-neutral-400">
        Edit metadata
      </h3>
      <p className="mb-4 text-xs text-neutral-500">
        Saving locks this series' metadata — future scans won't overwrite your edits.
      </p>
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="text-sm">
          <span className="mb-1 block text-neutral-400">Name</span>
          <input value={name} onChange={(e) => setName(e.target.value)} className="input" />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-neutral-400">Publisher</span>
          <input value={publisher} onChange={(e) => setPublisher(e.target.value)} className="input" />
        </label>
        <label className="text-sm sm:col-span-2">
          <span className="mb-1 block text-neutral-400">Summary</span>
          <textarea
            value={summary}
            onChange={(e) => setSummary(e.target.value)}
            rows={3}
            className="input resize-y"
          />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-neutral-400">Genres (comma-separated)</span>
          <input value={genres} onChange={(e) => setGenres(e.target.value)} className="input" />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-neutral-400">Tags (comma-separated)</span>
          <input value={tags} onChange={(e) => setTags(e.target.value)} className="input" />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-neutral-400">Language (ISO)</span>
          <input
            value={language}
            onChange={(e) => setLanguage(e.target.value)}
            placeholder="e.g. en, ja"
            className="input"
          />
        </label>
        <label className="text-sm">
          <span className="mb-1 block text-neutral-400">Age rating</span>
          <input
            value={ageRating}
            onChange={(e) => setAgeRating(e.target.value)}
            placeholder="e.g. Teen, Mature 17+"
            className="input"
          />
        </label>
      </div>

      <div className="mt-5 border-t border-neutral-800 pt-4">
        <span className="mb-1 block text-sm text-neutral-400">Cover image</span>
        <p className="mb-3 text-xs text-neutral-500">
          Uploads are saved into the series folder as <code>folder.jpg</code>, so they persist across
          app updates and library re-scans.
        </p>
        <div className="flex items-center gap-4">
          <div className="h-32 w-24 shrink-0 overflow-hidden rounded-lg bg-neutral-800 ring-1 ring-white/5">
            {coverPreview ? (
              <img src={coverPreview} alt="New cover preview" className="h-full w-full object-cover" />
            ) : series.hasCover ? (
              <img
                src={`/api/series/${series.id}/cover`}
                alt={series.name}
                className="h-full w-full object-cover"
              />
            ) : (
              <div className="flex h-full items-center justify-center text-xs text-neutral-600">
                No cover
              </div>
            )}
          </div>
          <div className="flex flex-col gap-2">
            <input
              type="file"
              accept="image/*"
              onChange={(e) => pickCover(e.target.files?.[0] ?? null)}
              className="text-sm text-neutral-300 file:mr-3 file:rounded-lg file:border-0 file:bg-neutral-800 file:px-3 file:py-1.5 file:text-sm file:text-neutral-200 hover:file:bg-neutral-700"
            />
            <div className="flex items-center gap-2">
              <button
                onClick={uploadCover}
                disabled={!coverFile || uploadingCover}
                className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
              >
                {uploadingCover ? "Uploading…" : "Upload cover"}
              </button>
              {coverMsg && <span className="text-xs text-teal-mint">{coverMsg}</span>}
            </div>
          </div>
        </div>
      </div>

      {error && <p className="mt-3 text-sm text-red-400">{error}</p>}
      <div className="mt-4 flex gap-2">
        <button
          onClick={save}
          disabled={saving}
          className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
        >
          {saving ? "Saving…" : "Save"}
        </button>
        <button
          onClick={onCancel}
          className="rounded-xl border border-neutral-700 px-4 py-1.5 text-sm text-neutral-300 hover:bg-neutral-800"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
