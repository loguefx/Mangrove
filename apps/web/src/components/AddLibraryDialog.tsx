import { useState } from "react";
import { api } from "../api";

const LIBRARY_TYPES = ["Manga", "Comic", "Book", "Mixed"];

export function AddLibraryDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (libraryId: number) => void;
}) {
  const [name, setName] = useState("");
  const [type, setType] = useState(0);
  const [storageKind, setStorageKind] = useState(0); // 0 = Local, 1 = SMB
  const [paths, setPaths] = useState<string[]>([""]);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [domain, setDomain] = useState("");

  const [testResult, setTestResult] = useState<{ ok: boolean; message: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isSmb = storageKind === 1;
  const cleanPaths = paths.map((p) => p.trim()).filter(Boolean);

  const setPath = (i: number, value: string) =>
    setPaths((prev) => prev.map((p, idx) => (idx === i ? value : p)));
  const addPath = () => setPaths((prev) => [...prev, ""]);
  const removePath = (i: number) =>
    setPaths((prev) => (prev.length === 1 ? prev : prev.filter((_, idx) => idx !== i)));

  const test = async () => {
    setBusy(true);
    setTestResult(null);
    try {
      // Test every folder so the admin knows each one is reachable before creating the library.
      for (const p of cleanPaths) {
        const r = await api.testStorage({
          storageKind,
          rootPath: p,
          username: isSmb ? username : undefined,
          password: isSmb ? password : undefined,
          domain: isSmb ? domain : undefined,
        });
        if (!r.success) {
          setTestResult({ ok: false, message: `${p}: ${r.message}` });
          return;
        }
      }
      setTestResult({
        ok: true,
        message:
          cleanPaths.length > 1
            ? `All ${cleanPaths.length} folders are reachable.`
            : "Folder is reachable.",
      });
    } catch (err) {
      setTestResult({ ok: false, message: err instanceof Error ? err.message : "Test failed" });
    } finally {
      setBusy(false);
    }
  };

  const create = async () => {
    setBusy(true);
    setError(null);
    try {
      let credentialId: number | null = null;
      if (isSmb) {
        const cred = await api.createCredential({
          label: `${name || "SMB"} credentials`,
          username,
          password,
          domain: domain || null,
          kind: 1,
        });
        credentialId = cred.id;
      }
      const lib = await api.createLibrary({
        name,
        type,
        storageKind,
        paths: cleanPaths,
        credentialId,
        folderWatch: false,
      });
      onCreated(lib.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create library");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="w-full max-w-lg rounded-2xl bg-neutral-900 p-6 shadow-2xl ring-1 ring-white/10">
        <h2 className="mb-4 text-lg font-semibold">Add library</h2>

        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <label className="block">
              <span className="mb-1 block text-sm text-neutral-400">Name</span>
              <input className="input" value={name} onChange={(e) => setName(e.target.value)} />
            </label>
            <label className="block">
              <span className="mb-1 block text-sm text-neutral-400">Type</span>
              <select
                className="input"
                value={type}
                onChange={(e) => setType(Number(e.target.value))}
              >
                {LIBRARY_TYPES.map((t, i) => (
                  <option key={t} value={i}>
                    {t}
                  </option>
                ))}
              </select>
            </label>
          </div>

          <div className="flex gap-2">
            {["Local", "SMB / UNC"].map((label, i) => (
              <button
                key={label}
                onClick={() => setStorageKind(i)}
                className={`flex-1 rounded-xl px-3 py-2 text-sm transition ${
                  storageKind === i
                    ? "bg-teal/20 text-teal-mint ring-1 ring-teal-mint/40"
                    : "bg-neutral-800 text-neutral-300"
                }`}
              >
                {label}
              </button>
            ))}
          </div>

          <div className="space-y-2">
            <span className="block text-sm text-neutral-400">
              {isSmb ? "Folders (\\\\server\\share\\folder or smb://…)" : "Folders"}
            </span>
            {paths.map((p, i) => (
              <div key={i} className="flex items-center gap-2">
                <input
                  className="input flex-1"
                  value={p}
                  onChange={(e) => setPath(i, e.target.value)}
                  placeholder={isSmb ? "\\\\NAS\\Manga" : "C:\\Manga"}
                />
                <button
                  onClick={() => removePath(i)}
                  disabled={paths.length === 1}
                  title="Remove folder"
                  className="rounded-lg bg-neutral-800 px-3 py-2 text-sm text-neutral-300 hover:bg-neutral-700 disabled:opacity-30"
                >
                  −
                </button>
              </div>
            ))}
            <button
              onClick={addPath}
              className="text-sm text-teal-mint hover:underline"
            >
              + Add another folder
            </button>
            <p className="text-xs text-neutral-500">
              Add several folders when a library spans more than one location (e.g. a NAS share that ran
              out of space). A series can continue across folders.
            </p>
          </div>

          {isSmb && (
            <div className="grid grid-cols-3 gap-3">
              <label className="block">
                <span className="mb-1 block text-sm text-neutral-400">Username</span>
                <input
                  className="input"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                />
              </label>
              <label className="block">
                <span className="mb-1 block text-sm text-neutral-400">Password</span>
                <input
                  type="password"
                  className="input"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </label>
              <label className="block">
                <span className="mb-1 block text-sm text-neutral-400">Domain</span>
                <input className="input" value={domain} onChange={(e) => setDomain(e.target.value)} />
              </label>
            </div>
          )}

          {testResult && (
            <div
              className={`rounded-xl px-3 py-2 text-sm ${
                testResult.ok ? "bg-teal/10 text-teal-mint" : "bg-red-500/10 text-red-400"
              }`}
            >
              {testResult.message}
            </div>
          )}
          {error && <div className="text-sm text-red-400">{error}</div>}
        </div>

        <div className="mt-6 flex items-center justify-between">
          <button
            onClick={test}
            disabled={busy || cleanPaths.length === 0}
            className="rounded-xl bg-neutral-800 px-4 py-2 text-sm text-neutral-200 hover:bg-neutral-700 disabled:opacity-50"
          >
            Test connection
          </button>
          <div className="flex gap-2">
            <button
              onClick={onClose}
              className="rounded-xl px-4 py-2 text-sm text-neutral-400 hover:text-neutral-200"
            >
              Cancel
            </button>
            <button
              onClick={create}
              disabled={busy || !name || cleanPaths.length === 0}
              className="rounded-xl bg-teal px-4 py-2 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
            >
              Create
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
