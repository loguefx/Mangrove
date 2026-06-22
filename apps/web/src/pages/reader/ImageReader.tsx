import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, fetchImageObjectUrl, type ChapterManifestDto } from "../../api";
import { Spinner } from "../../components/Spinner";
import { usePreferences } from "../../preferences";

type Mode = "single" | "double" | "webtoon";
type Fit = "contain" | "width" | "height" | "original";
type Dir = "auto" | "ltr" | "rtl";

const MODE_KEY = "mangrove.reader.mode";
const FIT_KEY = "mangrove.reader.fit";
const DIR_PREF = "reader.dir";

function useReaderPrefs() {
  const [mode, setMode] = useState<Mode>(() => (localStorage.getItem(MODE_KEY) as Mode) || "single");
  const [fit, setFit] = useState<Fit>(() => (localStorage.getItem(FIT_KEY) as Fit) || "contain");
  useEffect(() => localStorage.setItem(MODE_KEY, mode), [mode]);
  useEffect(() => localStorage.setItem(FIT_KEY, fit), [fit]);

  // Reading direction is a per-user setting synced to the server, so it's identical
  // across the web app and the native app and on every device.
  const { getPref, setPref } = usePreferences();
  const dir = getPref(DIR_PREF, "auto") as Dir;
  const setDir = (d: Dir) => setPref(DIR_PREF, d);

  return { mode, setMode, fit, setFit, dir, setDir };
}

function fitClass(fit: Fit): string {
  switch (fit) {
    case "width":
      return "w-full h-auto";
    case "height":
      return "h-screen w-auto";
    case "original":
      return "max-w-none";
    default:
      return "max-h-screen max-w-full object-contain";
  }
}

/** Loads + caches page object URLs for a chapter; revokes on unmount. */
function usePageLoader(chapterId: number, pageCount: number) {
  const cache = useRef<Map<number, string>>(new Map());
  useEffect(() => {
    const c = cache.current;
    return () => {
      c.forEach((u) => URL.revokeObjectURL(u));
      c.clear();
    };
  }, [chapterId]);

  return useCallback(
    async (n: number): Promise<string | null> => {
      if (n < 0 || n >= pageCount) return null;
      const hit = cache.current.get(n);
      if (hit) return hit;
      const url = await fetchImageObjectUrl(`/api/chapters/${chapterId}/pages/${n}`);
      cache.current.set(n, url);
      return url;
    },
    [chapterId, pageCount]
  );
}

export default function ImageReader({
  manifest,
  startPage,
  onExit,
}: {
  manifest: ChapterManifestDto;
  startPage: number;
  onExit: () => void;
}) {
  const { mode, setMode, fit, setFit, dir, setDir } = useReaderPrefs();
  const [showSettings, setShowSettings] = useState(false);
  // "auto" follows the series' own direction; an explicit ltr/rtl overrides it everywhere.
  const rtl = dir === "auto" ? manifest.readingDirection === "rtl" : dir === "rtl";
  const id = manifest.id;
  const count = manifest.pageCount;
  const loadPage = usePageLoader(id, count);

  if (mode === "webtoon") {
    return (
      <ReaderChrome
        manifest={manifest}
        mode={mode}
        setMode={setMode}
        fit={fit}
        setFit={setFit}
        dir={dir}
        setDir={setDir}
        showSettings={showSettings}
        setShowSettings={setShowSettings}
        onExit={onExit}
        label="Webtoon"
      >
        <Webtoon id={id} count={count} startPage={startPage} loadPage={loadPage} fit={fit} />
      </ReaderChrome>
    );
  }

  return (
    <Paged
      manifest={manifest}
      mode={mode}
      setMode={setMode}
      fit={fit}
      setFit={setFit}
      dir={dir}
      setDir={setDir}
      showSettings={showSettings}
      setShowSettings={setShowSettings}
      rtl={rtl}
      startPage={startPage}
      loadPage={loadPage}
      onExit={onExit}
    />
  );
}

// ---- Paged (single + double) ----

function buildSpreads(count: number, double: boolean): number[][] {
  if (!double) return Array.from({ length: count }, (_, i) => [i]);
  const spreads: number[][] = [];
  if (count > 0) spreads.push([0]); // cover alone
  for (let i = 1; i < count; i += 2) {
    spreads.push(i + 1 < count ? [i, i + 1] : [i]);
  }
  return spreads;
}

function Paged(props: {
  manifest: ChapterManifestDto;
  mode: Mode;
  setMode: (m: Mode) => void;
  fit: Fit;
  setFit: (f: Fit) => void;
  dir: Dir;
  setDir: (d: Dir) => void;
  showSettings: boolean;
  setShowSettings: (b: boolean) => void;
  rtl: boolean;
  startPage: number;
  loadPage: (n: number) => Promise<string | null>;
  onExit: () => void;
}) {
  const { manifest, mode, rtl, startPage, loadPage, onExit } = props;
  const count = manifest.pageCount;
  const double = mode === "double";
  const spreads = useMemo(() => buildSpreads(count, double), [count, double]);

  const [spreadIdx, setSpreadIdx] = useState(() => {
    const found = spreads.findIndex((s) => s.includes(startPage));
    return found >= 0 ? found : 0;
  });
  const [urls, setUrls] = useState<(string | null)[]>([]);
  const [loading, setLoading] = useState(true);

  const current = spreads[spreadIdx] ?? [0];

  useEffect(() => {
    let active = true;
    setLoading(true);
    Promise.all(current.map((p) => loadPage(p))).then((res) => {
      if (!active) return;
      setUrls(res);
      setLoading(false);
      // Prefetch the next spread.
      const next = spreads[spreadIdx + 1];
      if (next) next.forEach((p) => void loadPage(p));
    });
    void api.saveProgress(manifest.id, current[0]).catch(() => undefined);
    return () => {
      active = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [spreadIdx, double, loadPage, manifest.id]);

  const goNext = useCallback(
    () => setSpreadIdx((i) => (i < spreads.length - 1 ? i + 1 : i)),
    [spreads.length]
  );
  const goPrev = useCallback(() => setSpreadIdx((i) => (i > 0 ? i - 1 : i)), []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "ArrowRight") rtl ? goPrev() : goNext();
      else if (e.key === "ArrowLeft") rtl ? goNext() : goPrev();
      else if (e.key === "Escape") onExit();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [rtl, goNext, goPrev, onExit]);

  const onClickViewport = (e: React.MouseEvent) => {
    const x = e.clientX - e.currentTarget.getBoundingClientRect().left;
    const rightHalf = x > e.currentTarget.clientWidth / 2;
    if (rightHalf) (rtl ? goPrev : goNext)();
    else (rtl ? goNext : goPrev)();
  };

  // In double-page RTL the lower-index page sits on the right.
  const ordered = double && rtl ? [...current].reverse() : current;

  return (
    <ReaderChrome
      manifest={manifest}
      mode={props.mode}
      setMode={props.setMode}
      fit={props.fit}
      setFit={props.setFit}
      dir={props.dir}
      setDir={props.setDir}
      showSettings={props.showSettings}
      setShowSettings={props.setShowSettings}
      onExit={onExit}
      label={`Page ${current[0] + 1}${current.length > 1 ? `–${current[1] + 1}` : ""} / ${count} · ${
        rtl ? "RTL" : "LTR"
      }`}
      footer={
        <div className="flex items-center justify-center gap-4">
          <button
            onClick={goPrev}
            disabled={spreadIdx === 0}
            className="rounded-lg bg-neutral-800/80 px-4 py-1.5 text-sm text-neutral-200 disabled:opacity-40"
          >
            {rtl ? "Next" : "Previous"}
          </button>
          <input
            type="range"
            min={0}
            max={Math.max(0, spreads.length - 1)}
            value={spreadIdx}
            onChange={(e) => setSpreadIdx(Number(e.target.value))}
            className="w-64 accent-teal-mint"
            dir={rtl ? "rtl" : "ltr"}
          />
          <button
            onClick={goNext}
            disabled={spreadIdx >= spreads.length - 1}
            className="rounded-lg bg-neutral-800/80 px-4 py-1.5 text-sm text-neutral-200 disabled:opacity-40"
          >
            {rtl ? "Previous" : "Next"}
          </button>
        </div>
      }
    >
      <div
        className="flex h-full flex-1 cursor-pointer items-center justify-center gap-1 overflow-auto"
        onClick={onClickViewport}
      >
        {loading ? (
          <Spinner />
        ) : (
          ordered.map((p, i) =>
            urls[double && rtl ? current.length - 1 - i : i] ? (
              <img
                key={p}
                src={urls[double && rtl ? current.length - 1 - i : i] ?? undefined}
                alt={`Page ${p + 1}`}
                className={`${fitClass(props.fit)} select-none`}
                draggable={false}
              />
            ) : null
          )
        )}
      </div>
    </ReaderChrome>
  );
}

// ---- Webtoon (continuous scroll) ----

function Webtoon({
  id,
  count,
  startPage,
  loadPage,
  fit,
}: {
  id: number;
  count: number;
  startPage: number;
  loadPage: (n: number) => Promise<string | null>;
  fit: Fit;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [loaded, setLoaded] = useState<Map<number, string>>(new Map());
  const observer = useRef<IntersectionObserver | null>(null);

  useEffect(() => {
    observer.current = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (!entry.isIntersecting) return;
          const n = Number((entry.target as HTMLElement).dataset.page);
          if (loaded.has(n)) return;
          void loadPage(n).then((url) => {
            if (url) setLoaded((m) => new Map(m).set(n, url));
          });
        });
      },
      { rootMargin: "1500px 0px" }
    );
    return () => observer.current?.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadPage]);

  // Persist progress (debounced) based on which page is centered.
  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    let timer: number | undefined;
    const onScroll = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        const mid = el.scrollTop + el.clientHeight / 2;
        const slots = Array.from(el.querySelectorAll<HTMLElement>("[data-page]"));
        const cur = slots.find((s) => s.offsetTop <= mid && s.offsetTop + s.offsetHeight >= mid);
        if (cur) void api.saveProgress(id, Number(cur.dataset.page)).catch(() => undefined);
      }, 400);
    };
    el.addEventListener("scroll", onScroll);
    return () => {
      el.removeEventListener("scroll", onScroll);
      window.clearTimeout(timer);
    };
  }, [id]);

  const widthClass = fit === "original" ? "max-w-none" : "mx-auto w-full max-w-3xl";

  return (
    <div ref={containerRef} className="h-full overflow-y-auto bg-black">
      {Array.from({ length: count }, (_, n) => (
        <div
          key={n}
          data-page={n}
          ref={(node) => {
            if (node) observer.current?.observe(node);
          }}
          className="flex min-h-[40vh] items-center justify-center"
          style={n === startPage ? { scrollMarginTop: 0 } : undefined}
        >
          {loaded.get(n) ? (
            <img src={loaded.get(n)} alt={`Page ${n + 1}`} className={widthClass} draggable={false} />
          ) : (
            <Spinner label={`Page ${n + 1}`} />
          )}
        </div>
      ))}
    </div>
  );
}

// ---- Shared chrome (header/footer/settings) ----

function ReaderChrome({
  manifest,
  mode,
  setMode,
  fit,
  setFit,
  dir,
  setDir,
  showSettings,
  setShowSettings,
  onExit,
  label,
  footer,
  children,
}: {
  manifest: ChapterManifestDto;
  mode: Mode;
  setMode: (m: Mode) => void;
  fit: Fit;
  setFit: (f: Fit) => void;
  dir: Dir;
  setDir: (d: Dir) => void;
  showSettings: boolean;
  setShowSettings: (b: boolean) => void;
  onExit: () => void;
  label: string;
  footer?: React.ReactNode;
  children: React.ReactNode;
}) {
  void manifest;
  return (
    <div className="relative flex h-screen flex-col bg-black">
      <header className="absolute inset-x-0 top-0 z-10 flex items-center justify-between bg-gradient-to-b from-black/80 to-transparent px-4 py-3 text-sm text-neutral-200">
        <button onClick={onExit} className="hover:text-teal-mint">
          ← Back
        </button>
        <span>{label}</span>
        <button onClick={() => setShowSettings(!showSettings)} className="hover:text-teal-mint" aria-label="Settings">
          ⚙ Settings
        </button>
      </header>

      {showSettings && (
        <div className="absolute right-4 top-12 z-20 w-56 rounded-xl border border-neutral-700 bg-neutral-900/95 p-4 text-sm text-neutral-200 shadow-xl">
          <label className="mb-1 block text-xs uppercase tracking-wide text-neutral-400">Layout</label>
          <select
            value={mode}
            onChange={(e) => setMode(e.target.value as Mode)}
            className="mb-3 w-full rounded-lg bg-neutral-800 px-2 py-1.5"
          >
            <option value="single">Single page</option>
            <option value="double">Double page</option>
            <option value="webtoon">Webtoon (scroll)</option>
          </select>
          <label className="mb-1 block text-xs uppercase tracking-wide text-neutral-400">Fit</label>
          <select
            value={fit}
            onChange={(e) => setFit(e.target.value as Fit)}
            className="mb-3 w-full rounded-lg bg-neutral-800 px-2 py-1.5"
          >
            <option value="contain">Fit screen</option>
            <option value="width">Fit width</option>
            <option value="height">Fit height</option>
            <option value="original">Original size</option>
          </select>
          <label className="mb-1 block text-xs uppercase tracking-wide text-neutral-400">
            Reading direction
          </label>
          <select
            value={dir}
            onChange={(e) => setDir(e.target.value as Dir)}
            className="w-full rounded-lg bg-neutral-800 px-2 py-1.5"
          >
            <option value="rtl">Right to left (manga)</option>
            <option value="ltr">Left to right</option>
            <option value="auto">Match series</option>
          </select>
        </div>
      )}

      <div className="flex flex-1 overflow-hidden">{children}</div>

      {footer && (
        <footer className="absolute inset-x-0 bottom-0 z-10 bg-gradient-to-t from-black/80 to-transparent px-4 py-3">
          {footer}
        </footer>
      )}
    </div>
  );
}
