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

const ZOOM_MIN = 0.5;
const ZOOM_MAX = 5;
const ZOOM_STEP = 0.25;
const clampZoom = (z: number) => Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.round(z * 100) / 100));

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
  const [zoom, setZoom] = useState(1);
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
        zoom={zoom}
        setZoom={setZoom}
        onExit={onExit}
        label="Webtoon"
      >
        <Webtoon id={id} count={count} startPage={startPage} loadPage={loadPage} fit={fit} zoom={zoom} />
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
      zoom={zoom}
      setZoom={setZoom}
      rtl={rtl}
      startPage={startPage}
      loadPage={loadPage}
      onExit={onExit}
    />
  );
}

/** A scroll container that fits a page at zoom 1 and lets you pan when zoomed in. */
function ZoomViewport({
  containerRef,
  zoom,
  setZoom,
  onTap,
  children,
}: {
  containerRef: React.RefObject<HTMLDivElement>;
  zoom: number;
  setZoom: (z: number) => void;
  onTap?: (e: React.MouseEvent) => void;
  children: React.ReactNode;
}) {
  const drag = useRef<{ x: number; y: number; sl: number; st: number; moved: boolean } | null>(null);

  const onWheel = (e: React.WheelEvent) => {
    if (!(e.ctrlKey || e.metaKey)) return; // plain wheel scrolls/pans
    e.preventDefault();
    setZoom(clampZoom(zoom + (e.deltaY < 0 ? ZOOM_STEP : -ZOOM_STEP)));
  };

  const onPointerDown = (e: React.PointerEvent) => {
    if (zoom <= 1 || e.button !== 0) return;
    const el = containerRef.current;
    if (!el) return;
    drag.current = { x: e.clientX, y: e.clientY, sl: el.scrollLeft, st: el.scrollTop, moved: false };
    el.setPointerCapture(e.pointerId);
  };
  const onPointerMove = (e: React.PointerEvent) => {
    const d = drag.current;
    const el = containerRef.current;
    if (!d || !el) return;
    const dx = e.clientX - d.x;
    const dy = e.clientY - d.y;
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) d.moved = true;
    el.scrollLeft = d.sl - dx;
    el.scrollTop = d.st - dy;
  };
  const onPointerUp = (e: React.PointerEvent) => {
    drag.current = null;
    containerRef.current?.releasePointerCapture?.(e.pointerId);
  };

  const handleClick = (e: React.MouseEvent) => {
    if (drag.current?.moved) return; // finished a pan, not a tap
    if (e.detail > 1) return; // part of a double-click (zoom toggle)
    if (zoom > 1) return; // taps don't turn pages while zoomed in
    onTap?.(e);
  };
  const handleDoubleClick = () => setZoom(zoom > 1 ? 1 : 2.5);

  return (
    <div
      ref={containerRef}
      className={`relative h-full w-full overflow-auto ${zoom > 1 ? "cursor-grab active:cursor-grabbing" : "cursor-pointer"}`}
      onWheel={onWheel}
      onClick={handleClick}
      onDoubleClick={handleDoubleClick}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
    >
      <div className="flex min-h-full min-w-full">{children}</div>
    </div>
  );
}

/**
 * Renders a page at an explicit pixel size = (fit-to-viewport scale) × zoom.
 * Using a real size (not just max-* caps) means zoom genuinely enlarges the page
 * and the scroll container grows so every part stays reachable.
 */
function ZoomImage({
  src,
  alt,
  fit,
  zoom,
  frac,
  containerRef,
}: {
  src: string;
  alt: string;
  fit: Fit;
  zoom: number;
  frac: number; // share of viewport width this page may use (1 single, 0.5 double)
  containerRef: React.RefObject<HTMLElement>;
}) {
  const [nat, setNat] = useState<{ w: number; h: number } | null>(null);
  const [box, setBox] = useState<{ w: number; h: number } | null>(null);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const update = () => setBox({ w: el.clientWidth, h: el.clientHeight });
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, [containerRef]);

  const style = useMemo<React.CSSProperties>(() => {
    if (!nat || !box || nat.w === 0 || nat.h === 0) return { maxWidth: "100%", maxHeight: "100%" };
    const availW = box.w * frac;
    let base: number; // scale that makes the page "fit" at zoom 1
    switch (fit) {
      case "width":
        base = availW / nat.w;
        break;
      case "height":
        base = box.h / nat.h;
        break;
      case "original":
        base = 1;
        break;
      default:
        base = Math.min(availW / nat.w, box.h / nat.h);
    }
    const s = base * zoom;
    return { width: `${Math.round(nat.w * s)}px`, height: `${Math.round(nat.h * s)}px` };
  }, [nat, box, fit, zoom, frac]);

  return (
    <img
      src={src}
      alt={alt}
      className="m-auto block select-none"
      style={style}
      draggable={false}
      onLoad={(e) => setNat({ w: e.currentTarget.naturalWidth, h: e.currentTarget.naturalHeight })}
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
  zoom: number;
  setZoom: (z: number) => void;
  rtl: boolean;
  startPage: number;
  loadPage: (n: number) => Promise<string | null>;
  onExit: () => void;
}) {
  const { manifest, mode, zoom, setZoom, rtl, startPage, loadPage, onExit } = props;
  const count = manifest.pageCount;
  const double = mode === "double";
  const spreads = useMemo(() => buildSpreads(count, double), [count, double]);
  const viewportRef = useRef<HTMLDivElement>(null);

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
      else if (e.key === "+" || e.key === "=") setZoom(clampZoom(zoom + ZOOM_STEP));
      else if (e.key === "-" || e.key === "_") setZoom(clampZoom(zoom - ZOOM_STEP));
      else if (e.key === "0") setZoom(1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [rtl, goNext, goPrev, onExit, zoom, setZoom]);

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
      zoom={zoom}
      setZoom={setZoom}
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
      <ZoomViewport containerRef={viewportRef} zoom={zoom} setZoom={setZoom} onTap={onClickViewport}>
        {loading ? (
          <div className="m-auto">
            <Spinner />
          </div>
        ) : (
          ordered.map((p, i) => {
            const url = urls[double && rtl ? current.length - 1 - i : i];
            return url ? (
              <ZoomImage
                key={p}
                src={url}
                alt={`Page ${p + 1}`}
                fit={props.fit}
                zoom={zoom}
                frac={1 / ordered.length}
                containerRef={viewportRef}
              />
            ) : null;
          })
        )}
      </ZoomViewport>
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
  zoom,
}: {
  id: number;
  count: number;
  startPage: number;
  loadPage: (n: number) => Promise<string | null>;
  fit: Fit;
  zoom: number;
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

  // Webtoon zoom widens the strip; the column stays centered and scrolls vertically.
  const imgStyle =
    fit === "original" ? { maxWidth: "none" } : { width: "100%", maxWidth: `${48 * zoom}rem` };

  return (
    <div ref={containerRef} className="h-full overflow-auto bg-black">
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
            <img
              src={loaded.get(n)}
              alt={`Page ${n + 1}`}
              className="mx-auto"
              style={imgStyle}
              draggable={false}
            />
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
  zoom,
  setZoom,
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
  zoom: number;
  setZoom: (z: number) => void;
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

      {/* Floating zoom controls (work in every layout). */}
      <div className="absolute bottom-20 right-4 z-10 flex flex-col items-stretch overflow-hidden rounded-xl border border-neutral-700 bg-neutral-900/90 text-neutral-200 shadow-xl">
        <button
          onClick={() => setZoom(clampZoom(zoom + ZOOM_STEP))}
          className="px-3 py-2 text-lg leading-none hover:bg-neutral-800"
          aria-label="Zoom in"
        >
          +
        </button>
        <button
          onClick={() => setZoom(1)}
          className="border-y border-neutral-700 px-3 py-1.5 text-[11px] hover:bg-neutral-800"
          aria-label="Reset zoom"
        >
          {Math.round(zoom * 100)}%
        </button>
        <button
          onClick={() => setZoom(clampZoom(zoom - ZOOM_STEP))}
          className="px-3 py-2 text-lg leading-none hover:bg-neutral-800"
          aria-label="Zoom out"
        >
          −
        </button>
      </div>

      {footer && (
        <footer className="absolute inset-x-0 bottom-0 z-10 bg-gradient-to-t from-black/80 to-transparent px-4 py-3">
          {footer}
        </footer>
      )}
    </div>
  );
}
