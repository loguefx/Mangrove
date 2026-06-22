import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  api,
  bookContentUrl,
  fetchBlobObjectUrl,
  fetchTextWithAuth,
  type EpubManifestDto,
} from "../../api";
import { Spinner } from "../../components/Spinner";

type Theme = "light" | "sepia" | "dark";

const THEME_KEY = "mangrove.epub.theme";
const FONT_KEY = "mangrove.epub.fontsize";

const THEMES: Record<Theme, { bg: string; fg: string; link: string }> = {
  light: { bg: "#fafafa", fg: "#1a1a1a", link: "#0a7d72" },
  sepia: { bg: "#f4ecd8", fg: "#5b4636", link: "#8a5a2b" },
  dark: { bg: "#1a1a1a", fg: "#d6d6d6", link: "#5ad1c0" },
};

/** Resolves a resource href relative to the spine document path, into a root-relative epub path. */
function resolveEpubHref(baseHref: string, rel: string): string | null {
  if (/^(https?:|data:|blob:|mailto:|#)/i.test(rel) || rel.trim() === "") return null;
  try {
    const u = new URL(rel, `http://epub/${baseHref}`);
    return decodeURIComponent(u.pathname.replace(/^\//, ""));
  } catch {
    return null;
  }
}

export default function EpubReader({
  chapterId,
  pageCount,
  startPage,
  onExit,
}: {
  chapterId: number;
  pageCount: number;
  startPage: number;
  onExit: () => void;
}) {
  const [manifest, setManifest] = useState<EpubManifestDto | null>(null);
  const [spineIdx, setSpineIdx] = useState(startPage);
  const [srcDoc, setSrcDoc] = useState<string>("");
  const [loading, setLoading] = useState(true);
  const [showToc, setShowToc] = useState(false);
  const [showSettings, setShowSettings] = useState(false);

  const [theme, setTheme] = useState<Theme>(() => (localStorage.getItem(THEME_KEY) as Theme) || "light");
  const [fontSize, setFontSize] = useState<number>(() => Number(localStorage.getItem(FONT_KEY)) || 100);
  useEffect(() => localStorage.setItem(THEME_KEY, theme), [theme]);
  useEffect(() => localStorage.setItem(FONT_KEY, String(fontSize)), [fontSize]);

  const blobUrls = useRef<string[]>([]);

  useEffect(() => {
    api.bookManifest(chapterId).then(setManifest);
  }, [chapterId]);

  const spineLength = manifest?.spine.length ?? pageCount;
  const themeCss = useMemo(() => {
    const t = THEMES[theme];
    return `html,body{margin:0;background:${t.bg}!important;color:${t.fg}!important;}
      body{font-size:${fontSize}%!important;line-height:1.6;padding:2rem 1.25rem;max-width:42rem;margin:0 auto;
      font-family:Georgia,'Times New Roman',serif;}
      a{color:${t.link}!important;} img{max-width:100%;height:auto;}`;
  }, [theme, fontSize]);

  const renderSpine = useCallback(
    async (idx: number) => {
      if (!manifest) return;
      const item = manifest.spine[idx];
      if (!item) return;
      setLoading(true);

      // Revoke previous spine's blob URLs.
      blobUrls.current.forEach((u) => URL.revokeObjectURL(u));
      blobUrls.current = [];

      try {
        const html = await fetchTextWithAuth(bookContentUrl(chapterId, item.href));
        const doc = new DOMParser().parseFromString(html, "application/xhtml+xml");
        const fallback = doc.querySelector("parsererror")
          ? new DOMParser().parseFromString(html, "text/html")
          : doc;

        await inlineImages(fallback, chapterId, item.href, blobUrls.current);
        await inlineStylesheets(fallback, chapterId, item.href, blobUrls.current);

        const head = fallback.querySelector("head") ?? fallback.documentElement;
        const style = fallback.createElement("style");
        style.textContent = themeCss;
        head.appendChild(style);

        setSrcDoc("<!DOCTYPE html>" + (fallback.documentElement?.outerHTML ?? html));
      } catch {
        setSrcDoc(`<html><body style="background:${THEMES[theme].bg};color:${THEMES[theme].fg};padding:2rem">
          <p>Could not load this section.</p></body></html>`);
      } finally {
        setLoading(false);
      }
    },
    [manifest, chapterId, themeCss, theme]
  );

  useEffect(() => {
    if (manifest) void renderSpine(spineIdx);
  }, [manifest, spineIdx, renderSpine]);

  useEffect(() => {
    if (manifest) void api.saveProgress(chapterId, spineIdx).catch(() => undefined);
  }, [manifest, chapterId, spineIdx]);

  useEffect(() => {
    return () => blobUrls.current.forEach((u) => URL.revokeObjectURL(u));
  }, []);

  const goNext = useCallback(
    () => setSpineIdx((i) => (i < spineLength - 1 ? i + 1 : i)),
    [spineLength]
  );
  const goPrev = useCallback(() => setSpineIdx((i) => (i > 0 ? i - 1 : i)), []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "ArrowRight") goNext();
      else if (e.key === "ArrowLeft") goPrev();
      else if (e.key === "Escape") onExit();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [goNext, goPrev, onExit]);

  const jumpToHref = (href: string) => {
    if (!manifest) return;
    const base = href.split("#")[0];
    const target = manifest.spine.findIndex((s) => s.href === base || s.href.endsWith(base));
    if (target >= 0) setSpineIdx(target);
    setShowToc(false);
  };

  if (!manifest) {
    return (
      <div className="flex h-screen items-center justify-center bg-neutral-900">
        <Spinner label="Loading book…" />
      </div>
    );
  }

  return (
    <div className="relative flex h-screen flex-col" style={{ background: THEMES[theme].bg }}>
      <header className="flex items-center justify-between border-b border-black/10 bg-black/5 px-4 py-2 text-sm">
        <button onClick={onExit} className="hover:text-teal-mint">
          ← Back
        </button>
        <span className="truncate px-2 font-medium" style={{ color: THEMES[theme].fg }}>
          {manifest.title}
        </span>
        <div className="flex gap-3">
          <button onClick={() => { setShowToc(!showToc); setShowSettings(false); }} className="hover:text-teal-mint">
            Contents
          </button>
          <button onClick={() => { setShowSettings(!showSettings); setShowToc(false); }} className="hover:text-teal-mint">
            Aa
          </button>
        </div>
      </header>

      {showToc && (
        <div className="absolute right-4 top-12 z-20 max-h-[70vh] w-72 overflow-y-auto rounded-xl border border-neutral-300 bg-white p-2 text-sm text-neutral-800 shadow-xl">
          {manifest.toc.length === 0 ? (
            <p className="p-2 text-neutral-500">No table of contents.</p>
          ) : (
            manifest.toc.map((t, i) => (
              <button
                key={i}
                onClick={() => jumpToHref(t.href)}
                className="block w-full rounded px-2 py-1.5 text-left hover:bg-neutral-100"
              >
                {t.label}
              </button>
            ))
          )}
        </div>
      )}

      {showSettings && (
        <div className="absolute right-4 top-12 z-20 w-60 rounded-xl border border-neutral-300 bg-white p-4 text-sm text-neutral-800 shadow-xl">
          <label className="mb-1 block text-xs uppercase tracking-wide text-neutral-500">Theme</label>
          <div className="mb-3 flex gap-2">
            {(["light", "sepia", "dark"] as Theme[]).map((t) => (
              <button
                key={t}
                onClick={() => setTheme(t)}
                className={`flex-1 rounded-lg border px-2 py-1.5 capitalize ${
                  theme === t ? "border-teal-mint ring-2 ring-teal-mint/40" : "border-neutral-300"
                }`}
                style={{ background: THEMES[t].bg, color: THEMES[t].fg }}
              >
                {t}
              </button>
            ))}
          </div>
          <label className="mb-1 block text-xs uppercase tracking-wide text-neutral-500">
            Font size · {fontSize}%
          </label>
          <div className="flex items-center gap-2">
            <button onClick={() => setFontSize((f) => Math.max(70, f - 10))} className="rounded bg-neutral-200 px-3 py-1">
              A−
            </button>
            <input
              type="range"
              min={70}
              max={180}
              step={10}
              value={fontSize}
              onChange={(e) => setFontSize(Number(e.target.value))}
              className="flex-1 accent-teal-mint"
            />
            <button onClick={() => setFontSize((f) => Math.min(180, f + 10))} className="rounded bg-neutral-200 px-3 py-1">
              A+
            </button>
          </div>
        </div>
      )}

      <div className="relative flex-1">
        {loading && (
          <div className="absolute inset-0 z-10 flex items-center justify-center" style={{ background: THEMES[theme].bg }}>
            <Spinner />
          </div>
        )}
        <iframe
          title="EPUB content"
          sandbox="allow-same-origin"
          srcDoc={srcDoc}
          className="h-full w-full border-0"
        />
      </div>

      <footer className="flex items-center justify-center gap-4 border-t border-black/10 bg-black/5 px-4 py-2 text-sm">
        <button
          onClick={goPrev}
          disabled={spineIdx === 0}
          className="rounded-lg bg-neutral-700 px-4 py-1.5 text-neutral-100 disabled:opacity-40"
        >
          Previous
        </button>
        <span style={{ color: THEMES[theme].fg }}>
          {spineIdx + 1} / {spineLength}
        </span>
        <button
          onClick={goNext}
          disabled={spineIdx >= spineLength - 1}
          className="rounded-lg bg-neutral-700 px-4 py-1.5 text-neutral-100 disabled:opacity-40"
        >
          Next
        </button>
      </footer>
    </div>
  );
}

async function inlineImages(doc: Document, chapterId: number, baseHref: string, sink: string[]) {
  const imgs = Array.from(doc.querySelectorAll("img, image"));
  await Promise.all(
    imgs.map(async (el) => {
      const attr = el.hasAttribute("src") ? "src" : "xlink:href";
      const rel = el.getAttribute("src") ?? el.getAttribute("xlink:href") ?? el.getAttribute("href");
      if (!rel) return;
      const resolved = resolveEpubHref(baseHref, rel);
      if (!resolved) return;
      try {
        const url = await fetchBlobObjectUrl(bookContentUrl(chapterId, resolved));
        sink.push(url);
        if (el.hasAttribute("href")) el.setAttribute("href", url);
        else el.setAttribute(attr, url);
      } catch {
        /* ignore missing resource */
      }
    })
  );
}

async function inlineStylesheets(doc: Document, chapterId: number, baseHref: string, sink: string[]) {
  const links = Array.from(doc.querySelectorAll('link[rel="stylesheet"]'));
  await Promise.all(
    links.map(async (link) => {
      const rel = link.getAttribute("href");
      if (!rel) return;
      const resolved = resolveEpubHref(baseHref, rel);
      if (!resolved) return;
      try {
        let css = await fetchTextWithAuth(bookContentUrl(chapterId, resolved));
        css = await rewriteCssUrls(css, chapterId, resolved, sink);
        const style = doc.createElement("style");
        style.textContent = css;
        link.replaceWith(style);
      } catch {
        link.remove();
      }
    })
  );
}

async function rewriteCssUrls(css: string, chapterId: number, cssHref: string, sink: string[]): Promise<string> {
  const matches = [...css.matchAll(/url\(\s*['"]?([^'")]+)['"]?\s*\)/g)];
  for (const m of matches) {
    const resolved = resolveEpubHref(cssHref, m[1]);
    if (!resolved) continue;
    try {
      const url = await fetchBlobObjectUrl(bookContentUrl(chapterId, resolved));
      sink.push(url);
      css = css.replace(m[0], `url("${url}")`);
    } catch {
      /* ignore */
    }
  }
  return css;
}
