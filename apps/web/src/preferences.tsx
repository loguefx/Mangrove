import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { api } from "./api";
import { useAuth } from "./auth";

interface PrefsState {
  /** True once the server preferences have been loaded for the current user. */
  ready: boolean;
  getPref: (key: string, fallback: string) => string;
  setPref: (key: string, value: string) => void;
}

const PrefsContext = createContext<PrefsState | null>(null);

// localStorage mirror gives an instant value on boot before the server responds,
// and an offline fallback. The server copy is the source of truth and wins on load.
const CACHE_KEY = "mangrove.prefs.cache";

function readCache(): Record<string, string> {
  try {
    return JSON.parse(localStorage.getItem(CACHE_KEY) || "{}");
  } catch {
    return {};
  }
}

export function PreferencesProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [prefs, setPrefs] = useState<Record<string, string>>(() => readCache());
  const [ready, setReady] = useState(false);
  const saveTimer = useRef<number | undefined>(undefined);
  const dirty = useRef<Record<string, string | null>>({});

  useEffect(() => {
    if (!user) {
      setReady(false);
      return;
    }
    let cancelled = false;
    api
      .getPreferences()
      .then((server) => {
        if (cancelled) return;
        setPrefs(server);
        localStorage.setItem(CACHE_KEY, JSON.stringify(server));
      })
      .catch(() => undefined)
      .finally(() => {
        if (!cancelled) setReady(true);
      });
    return () => {
      cancelled = true;
    };
  }, [user]);

  const flush = useCallback(() => {
    const updates = dirty.current;
    dirty.current = {};
    if (Object.keys(updates).length === 0) return;
    api.savePreferences(updates).catch(() => undefined);
  }, []);

  const getPref = useCallback(
    (key: string, fallback: string) => prefs[key] ?? fallback,
    [prefs]
  );

  const setPref = useCallback(
    (key: string, value: string) => {
      setPrefs((prev) => {
        const next = { ...prev, [key]: value };
        localStorage.setItem(CACHE_KEY, JSON.stringify(next));
        return next;
      });
      dirty.current[key] = value;
      // Debounce writes so rapid toggles collapse into one request.
      window.clearTimeout(saveTimer.current);
      saveTimer.current = window.setTimeout(flush, 400);
    },
    [flush]
  );

  return (
    <PrefsContext.Provider value={{ ready, getPref, setPref }}>{children}</PrefsContext.Provider>
  );
}

export function usePreferences() {
  const ctx = useContext(PrefsContext);
  if (!ctx) throw new Error("usePreferences must be used within PreferencesProvider");
  return ctx;
}
