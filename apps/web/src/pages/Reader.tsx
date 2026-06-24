import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, type ChapterManifestDto } from "../api";
import { Spinner } from "../components/Spinner";
import ImageReader from "./reader/ImageReader";
import EpubReader from "./reader/EpubReader";

export default function Reader() {
  const { chapterId } = useParams();
  const id = Number(chapterId);
  const navigate = useNavigate();

  const [manifest, setManifest] = useState<ChapterManifestDto | null>(null);
  const [startPage, setStartPage] = useState<number | null>(null);

  useEffect(() => {
    let active = true;
    Promise.all([api.manifest(id), api.progressForChapter(id).catch(() => ({ page: 0 }))]).then(
      ([m, p]) => {
        if (!active) return;
        setManifest(m);
        setStartPage(Math.min(p.page ?? 0, Math.max(0, m.pageCount - 1)));
      }
    );
    return () => {
      active = false;
    };
  }, [id]);

  if (!manifest || startPage === null) {
    return (
      <div className="flex h-screen items-center justify-center bg-black">
        <Spinner label="Loading chapter…" />
      </div>
    );
  }

  const onExit = () => navigate(-1);

  if (manifest.mediaType === "epub") {
    return (
      <EpubReader chapterId={id} pageCount={manifest.pageCount} startPage={startPage} onExit={onExit} />
    );
  }

  // Seamlessly continue into the next chapter from the last page. Replacing the history entry keeps
  // the browser Back button pointing at the series page rather than every chapter you read through.
  const onNextChapter = manifest.nextChapterId
    ? () => navigate(`/reader/${manifest.nextChapterId}`, { replace: true })
    : undefined;

  return (
    <ImageReader
      manifest={manifest}
      startPage={startPage}
      onExit={onExit}
      onNextChapter={onNextChapter}
    />
  );
}
