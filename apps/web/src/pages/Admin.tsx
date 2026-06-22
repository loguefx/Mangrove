import { useEffect, useState } from "react";
import {
  api,
  type ActivityDto,
  type AdminUserDto,
  type LibraryDto,
  type ServerStatsDto,
  type SettingDto,
  type TaskLogDto,
} from "../api";
import { PageHeader } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";
import { AddLibraryDialog } from "../components/AddLibraryDialog";

const TABS = ["Users", "Activity", "Libraries", "Tasks", "Stats", "Settings"] as const;
type Tab = (typeof TABS)[number];

const AGE_TIERS = ["No restriction", "Everyone", "Everyone 10+", "Teen", "Mature 17+", "Adults Only 18+"];
const ROLES = ["Admin", "User", "ReadOnly"];

export default function Admin() {
  const [tab, setTab] = useState<Tab>("Users");

  return (
    <div className="mx-auto max-w-5xl p-6">
      <PageHeader title="Admin" />
      <div className="mb-6 flex gap-1 border-b border-neutral-800">
        {TABS.map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`-mb-px border-b-2 px-4 py-2 text-sm ${
              tab === t
                ? "border-teal-mint text-teal-mint"
                : "border-transparent text-neutral-400 hover:text-neutral-200"
            }`}
          >
            {t}
          </button>
        ))}
      </div>
      {tab === "Users" && <UsersTab />}
      {tab === "Activity" && <ActivityTab />}
      {tab === "Libraries" && <LibrariesTab />}
      {tab === "Tasks" && <TasksTab />}
      {tab === "Stats" && <StatsTab />}
      {tab === "Settings" && <SettingsTab />}
    </div>
  );
}

function UsersTab() {
  const [users, setUsers] = useState<AdminUserDto[]>([]);
  const [libraries, setLibraries] = useState<LibraryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [u, setU] = useState({ username: "", email: "", password: "", role: "User" });
  const [newLibIds, setNewLibIds] = useState<number[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = () => api.users().then(setUsers).catch(() => setUsers([]));

  useEffect(() => {
    Promise.all([load(), api.libraries().then(setLibraries).catch(() => setLibraries([]))]).finally(() =>
      setLoading(false)
    );
  }, []);

  const toggleNewLib = (id: number) =>
    setNewLibIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  const allChecked = libraries.length > 0 && newLibIds.length === libraries.length;

  const create = async () => {
    setError(null);
    try {
      await api.createUser({
        username: u.username,
        email: u.email || null,
        password: u.password,
        roles: [u.role],
        libraryIds: u.role === "Admin" ? undefined : newLibIds,
      });
      setU({ username: "", email: "", password: "", role: "User" });
      setNewLibIds([]);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Create failed");
    }
  };

  if (loading) return <Spinner />;

  return (
    <div>
      <div className="mb-6 rounded-2xl border border-neutral-800 bg-neutral-900/60 p-4">
        <h3 className="mb-3 text-sm font-semibold text-neutral-300">Create user</h3>
        <div className="grid gap-2 sm:grid-cols-4">
          <input
            placeholder="Username"
            value={u.username}
            onChange={(e) => setU({ ...u, username: e.target.value })}
            className="input"
          />
          <input
            placeholder="Email (optional)"
            value={u.email}
            onChange={(e) => setU({ ...u, email: e.target.value })}
            className="input"
          />
          <input
            type="password"
            placeholder="Password"
            value={u.password}
            onChange={(e) => setU({ ...u, password: e.target.value })}
            className="input"
          />
          <select value={u.role} onChange={(e) => setU({ ...u, role: e.target.value })} className="input">
            {ROLES.map((r) => (
              <option key={r}>{r}</option>
            ))}
          </select>
        </div>

        {u.role === "Admin" ? (
          <p className="mt-3 text-xs text-neutral-500">Admins automatically have access to all libraries.</p>
        ) : (
          <div className="mt-3">
            <div className="mb-1 flex items-center gap-3">
              <span className="text-xs font-semibold uppercase text-neutral-500">Library access</span>
              {libraries.length > 0 && (
                <button
                  type="button"
                  onClick={() => setNewLibIds(allChecked ? [] : libraries.map((l) => l.id))}
                  className="text-xs text-teal-mint hover:underline"
                >
                  {allChecked ? "Clear all" : "Select all"}
                </button>
              )}
            </div>
            {libraries.length === 0 ? (
              <p className="text-sm text-neutral-500">No libraries yet.</p>
            ) : (
              <div className="flex flex-wrap gap-3">
                {libraries.map((lib) => (
                  <label key={lib.id} className="flex items-center gap-1.5 text-sm text-neutral-300">
                    <input type="checkbox" checked={newLibIds.includes(lib.id)} onChange={() => toggleNewLib(lib.id)} />
                    {lib.name}
                  </label>
                ))}
              </div>
            )}
          </div>
        )}

        {error && <p className="mt-2 text-sm text-red-400">{error}</p>}
        <button
          onClick={create}
          className="mt-3 rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90"
        >
          Create
        </button>
      </div>

      <div className="space-y-3">
        {users.map((user) => (
          <UserRow key={user.id} user={user} libraries={libraries} onChanged={load} />
        ))}
      </div>
    </div>
  );
}

function UserRow({
  user,
  libraries,
  onChanged,
}: {
  user: AdminUserDto;
  libraries: LibraryDto[];
  onChanged: () => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [roles, setRoles] = useState<string[]>(user.roles);
  const [libIds, setLibIds] = useState<number[]>(user.libraryIds);
  const [maxAge, setMaxAge] = useState<number>(user.maxAgeRating ?? 0);
  const [includeUnknowns, setIncludeUnknowns] = useState<boolean>(user.includeUnknowns);
  const [pwd, setPwd] = useState("");
  const [msg, setMsg] = useState<string | null>(null);

  const isAdmin = roles.includes("Admin");
  const toggleRole = (r: string) =>
    setRoles((prev) => (prev.includes(r) ? prev.filter((x) => x !== r) : [...prev, r]));
  const toggleLib = (id: number) =>
    setLibIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));

  const save = async () => {
    setMsg(null);
    try {
      await api.updateUser(user.id, { roles, libraryIds: libIds, maxAgeRating: maxAge, includeUnknowns });
      setMsg("Saved");
      onChanged();
      setTimeout(() => setMsg(null), 1500);
    } catch (err) {
      setMsg(err instanceof Error ? err.message : "Save failed");
    }
  };

  const toggleLock = async () => {
    await api.updateUser(user.id, { isLocked: !user.isLocked });
    onChanged();
  };

  const resetPassword = async () => {
    if (!pwd.trim()) return;
    await api.resetPassword(user.id, pwd.trim());
    setPwd("");
    setMsg("Password reset");
    setTimeout(() => setMsg(null), 1500);
  };

  const remove = async () => {
    if (!confirm(`Delete user "${user.username}"?`)) return;
    await api.deleteUser(user.id);
    onChanged();
  };

  return (
    <div className="rounded-2xl border border-neutral-800 bg-neutral-900/40">
      <button
        onClick={() => setExpanded((v) => !v)}
        className="flex w-full items-center justify-between px-4 py-3 text-left"
      >
        <span>
          <span className="font-medium">{user.username}</span>
          <span className="ml-2 text-xs text-neutral-500">{user.roles.join(", ")}</span>
          {user.isLocked && <span className="ml-2 text-xs text-red-400">Locked</span>}
        </span>
        <span className="text-neutral-500">{expanded ? "▲" : "▼"}</span>
      </button>

      {expanded && (
        <div className="space-y-4 border-t border-neutral-800 p-4">
          <div>
            <div className="mb-1 text-xs font-semibold uppercase text-neutral-500">Roles</div>
            <div className="flex gap-3">
              {ROLES.map((r) => (
                <label key={r} className="flex items-center gap-1.5 text-sm text-neutral-300">
                  <input type="checkbox" checked={roles.includes(r)} onChange={() => toggleRole(r)} />
                  {r}
                </label>
              ))}
            </div>
          </div>

          <div>
            <div className="mb-1 flex items-center gap-3">
              <span className="text-xs font-semibold uppercase text-neutral-500">Library access</span>
              {libraries.length > 0 && (
                <button
                  type="button"
                  onClick={() =>
                    setLibIds(libIds.length === libraries.length ? [] : libraries.map((l) => l.id))
                  }
                  className="text-xs text-teal-mint hover:underline"
                >
                  {libIds.length === libraries.length ? "Clear all" : "Select all"}
                </button>
              )}
            </div>
            {isAdmin ? (
              <p className="text-sm text-neutral-500">Admins have access to all libraries.</p>
            ) : libraries.length === 0 ? (
              <p className="text-sm text-neutral-500">No libraries.</p>
            ) : (
              <div className="flex flex-wrap gap-3">
                {libraries.map((lib) => (
                  <label key={lib.id} className="flex items-center gap-1.5 text-sm text-neutral-300">
                    <input type="checkbox" checked={libIds.includes(lib.id)} onChange={() => toggleLib(lib.id)} />
                    {lib.name}
                  </label>
                ))}
              </div>
            )}
            <p className="mt-1 text-xs text-neutral-600">No boxes checked = no access to any library.</p>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <div>
              <div className="mb-1 text-xs font-semibold uppercase text-neutral-500">Age restriction</div>
              <select
                value={maxAge}
                onChange={(e) => setMaxAge(Number(e.target.value))}
                className="input"
              >
                {AGE_TIERS.map((label, i) => (
                  <option key={i} value={i}>
                    {label}
                  </option>
                ))}
              </select>
            </div>
            <label className="mt-5 flex items-center gap-1.5 text-sm text-neutral-300">
              <input
                type="checkbox"
                checked={includeUnknowns}
                onChange={(e) => setIncludeUnknowns(e.target.checked)}
              />
              Include unrated series
            </label>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={save}
              className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90"
            >
              Save
            </button>
            <button
              onClick={toggleLock}
              className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:bg-neutral-800"
            >
              {user.isLocked ? "Unlock" : "Lock"}
            </button>
            <button
              onClick={remove}
              className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-red-400 hover:border-red-500"
            >
              Delete
            </button>
            {msg && <span className="text-sm text-teal-mint">{msg}</span>}
          </div>

          <div className="flex items-center gap-2 border-t border-neutral-800 pt-3">
            <input
              type="password"
              placeholder="New password"
              value={pwd}
              onChange={(e) => setPwd(e.target.value)}
              className="input"
            />
            <button
              onClick={resetPassword}
              className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:bg-neutral-800"
            >
              Reset password
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function TasksTab() {
  const [logs, setLogs] = useState<TaskLogDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const load = () => api.tasks().then(setLogs).catch(() => setLogs([]));

  useEffect(() => {
    load().finally(() => setLoading(false));
  }, []);

  const scanAll = async () => {
    setScanning(true);
    setNotice(null);
    try {
      const r = await api.scanAll();
      setNotice(`Queued ${r.libraries} libraries for scanning. Progress appears under Tasks.`);
      await load();
    } catch (err) {
      setNotice(err instanceof Error ? err.message : "Scan failed");
    } finally {
      setScanning(false);
    }
  };

  if (loading) return <Spinner />;

  return (
    <div>
      <div className="mb-4 flex items-center gap-3">
        <button
          onClick={scanAll}
          disabled={scanning}
          className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
        >
          {scanning ? "Scanning…" : "Scan all libraries"}
        </button>
        <button
          onClick={load}
          className="rounded-xl border border-neutral-700 px-3 py-1.5 text-sm text-neutral-300 hover:bg-neutral-800"
        >
          Refresh
        </button>
        {notice && <span className="text-sm text-teal-mint">{notice}</span>}
      </div>

      <div className="overflow-hidden rounded-2xl ring-1 ring-white/5">
        <table className="w-full text-sm">
          <thead className="bg-neutral-900 text-left text-neutral-500">
            <tr>
              <th className="px-4 py-2">Kind</th>
              <th className="px-4 py-2">Target</th>
              <th className="px-4 py-2">Status</th>
              <th className="px-4 py-2">Message</th>
              <th className="px-4 py-2">Started</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-neutral-800">
            {logs.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-4 py-6 text-center text-neutral-500">
                  No tasks recorded yet.
                </td>
              </tr>
            ) : (
              logs.map((l) => (
                <tr key={l.id} className="bg-neutral-900/40">
                  <td className="px-4 py-2">{l.kind}</td>
                  <td className="px-4 py-2">{l.target}</td>
                  <td className="px-4 py-2">
                    <span
                      className={
                        l.status === "Failed"
                          ? "text-red-400"
                          : l.status === "Completed"
                          ? "text-teal-mint"
                          : "text-amber-400"
                      }
                    >
                      {l.status}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-neutral-400">{l.message}</td>
                  <td className="px-4 py-2 text-neutral-500">{new Date(l.startedAt).toLocaleString()}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function StatsTab() {
  const [stats, setStats] = useState<ServerStatsDto | null>(null);

  useEffect(() => {
    api.serverStats().then(setStats).catch(() => setStats(null));
  }, []);

  if (!stats) return <Spinner />;

  const cards: [string, string][] = [
    ["Users", String(stats.users)],
    ["Libraries", String(stats.libraries)],
    ["Series", String(stats.series)],
    ["Volumes", String(stats.volumes)],
    ["Chapters", String(stats.chapters)],
    ["Pages", String(stats.totalPages)],
    ["Storage", formatBytes(stats.totalBytes)],
  ];

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4">
      {cards.map(([label, value]) => (
        <div key={label} className="rounded-2xl border border-neutral-800 bg-neutral-900/40 p-4">
          <div className="text-2xl font-semibold text-teal-mint">{value}</div>
          <div className="text-sm text-neutral-500">{label}</div>
        </div>
      ))}
    </div>
  );
}

function timeAgo(iso: string): string {
  const then = new Date(iso).getTime();
  const secs = Math.max(0, Math.floor((Date.now() - then) / 1000));
  if (secs < 60) return "just now";
  const mins = Math.floor(secs / 60);
  if (mins < 60) return `${mins} min${mins === 1 ? "" : "s"} ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs} hour${hrs === 1 ? "" : "s"} ago`;
  const days = Math.floor(hrs / 24);
  if (days < 30) return `${days} day${days === 1 ? "" : "s"} ago`;
  return new Date(iso).toLocaleDateString();
}

function activityDescription(a: ActivityDto): { text: string; tone: string } {
  const series = a.seriesName ?? "Unknown series";
  const ch = `Ch. ${a.chapterNumber}${a.chapterTitle ? ` — ${a.chapterTitle}` : ""}`;
  if (a.status === "caught-up")
    return { text: `Caught up on ${series} — finished ${ch}`, tone: "text-teal-mint" };
  if (a.status === "finished")
    return { text: `Finished ${ch} of ${series}`, tone: "text-teal-mint" };
  return {
    text: `Left off on page ${a.page}${a.pageCount ? `/${a.pageCount}` : ""} of ${ch} — ${series}`,
    tone: "text-amber-400",
  };
}

function ActivityRow({ a, showUser = true }: { a: ActivityDto; showUser?: boolean }) {
  const d = activityDescription(a);
  return (
    <div className="flex items-center gap-3 rounded-2xl border border-neutral-800 bg-neutral-900/40 p-3">
      {showUser && (
        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-teal/20 text-sm font-semibold uppercase text-teal-mint">
          {a.username.slice(0, 2)}
        </div>
      )}
      <div className="min-w-0 flex-1">
        {showUser && <div className="truncate font-medium">{a.username}</div>}
        <div className={`truncate text-sm ${d.tone}`}>{d.text}</div>
      </div>
      <div className="shrink-0 text-xs text-neutral-400">{timeAgo(a.updatedAt)}</div>
    </div>
  );
}

function ActivityTab() {
  const [rows, setRows] = useState<ActivityDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const load = () => api.activity().then(setRows).catch(() => setRows([]));
    load().finally(() => setLoading(false));
    const iv = setInterval(() => {
      if (!document.hidden) load();
    }, 30000);
    return () => clearInterval(iv);
  }, []);

  if (loading) return <Spinner />;
  if (rows.length === 0)
    return <p className="text-sm text-neutral-500">No reading activity yet.</p>;

  // "Currently reading": each user's most recent unfinished chapter.
  const currentByUser = new Map<number, ActivityDto>();
  for (const r of rows) {
    if (r.status === "reading" && !currentByUser.has(r.userId)) currentByUser.set(r.userId, r);
  }
  const current = [...currentByUser.values()];

  return (
    <div className="space-y-6">
      <section>
        <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-neutral-400">
          Currently reading
        </h3>
        {current.length === 0 ? (
          <p className="text-sm text-neutral-500">Nobody has a chapter open right now.</p>
        ) : (
          <div className="space-y-2">
            {current.map((a) => (
              <div
                key={a.userId}
                className="flex items-center gap-3 rounded-2xl border border-neutral-800 bg-neutral-900/40 p-3"
              >
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-teal/20 text-sm font-semibold uppercase text-teal-mint">
                  {a.username.slice(0, 2)}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">{a.username}</div>
                  <div className="truncate text-sm text-neutral-400">
                    {a.seriesName ?? "Unknown series"} · Ch. {a.chapterNumber}
                    {a.chapterTitle ? ` — ${a.chapterTitle}` : ""}
                  </div>
                </div>
                <div className="shrink-0 text-right">
                  <div className="text-sm font-semibold text-amber-400">
                    p. {a.page}
                    {a.pageCount ? `/${a.pageCount}` : ""}
                  </div>
                  <div className="text-xs text-neutral-500">{timeAgo(a.updatedAt)}</div>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-neutral-400">
          Recent activity
        </h3>
        <div className="space-y-2">
          {rows.map((a) => (
            <ActivityRow key={`${a.userId}-${a.chapterId}`} a={a} />
          ))}
        </div>
      </section>
    </div>
  );
}

type SettingMeta = { label: string; help?: string; kind: "text" | "number" | "bool" };
const SETTING_META: Record<string, SettingMeta> = {
  "scan.intervalMinutes": {
    label: "Auto-scan interval (minutes)",
    help: "How often libraries are re-scanned so new chapters appear automatically. 0 disables it; values below 5 are treated as 5.",
    kind: "number",
  },
  "scan.onStartup": {
    label: "Scan on startup",
    help: "Run a scan shortly after the server starts.",
    kind: "bool",
  },
  "opds.enabled": { label: "Enable OPDS feed", kind: "bool" },
  "server.baseUrl": { label: "Public base URL", help: "Used for OPDS/links when behind a proxy.", kind: "text" },
  "theme.default": { label: "Default theme", kind: "text" },
};

function SettingsTab() {
  const [settings, setSettings] = useState<SettingDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    api.settings().then(setSettings).catch(() => setSettings([])).finally(() => setLoading(false));
  }, []);

  const setValue = (i: number, value: string) =>
    setSettings((prev) => prev.map((s, idx) => (idx === i ? { ...s, value } : s)));

  const save = async () => {
    await api.saveSettings(settings);
    setMsg("Saved");
    setTimeout(() => setMsg(null), 1500);
  };

  if (loading) return <Spinner />;

  return (
    <div className="space-y-4">
      {settings.map((s, i) => {
        const meta = SETTING_META[s.key] ?? { label: s.key, kind: "text" as const };
        return (
          <div key={s.key} className="flex items-start gap-3">
            <div className="w-56 pt-2">
              <label className="text-sm text-neutral-300">{meta.label}</label>
              {meta.help && <p className="mt-0.5 text-xs text-neutral-500">{meta.help}</p>}
            </div>
            <div className="flex-1">
              {meta.kind === "bool" ? (
                <label className="mt-1 inline-flex cursor-pointer items-center gap-2 text-sm text-neutral-300">
                  <input
                    type="checkbox"
                    checked={String(s.value).toLowerCase() === "true"}
                    onChange={(e) => setValue(i, e.target.checked ? "true" : "false")}
                    className="h-4 w-4 accent-teal"
                  />
                  {String(s.value).toLowerCase() === "true" ? "Enabled" : "Disabled"}
                </label>
              ) : (
                <input
                  type={meta.kind === "number" ? "number" : "text"}
                  min={meta.kind === "number" ? 0 : undefined}
                  value={s.value ?? ""}
                  onChange={(e) => setValue(i, e.target.value)}
                  className="input w-full"
                />
              )}
            </div>
          </div>
        );
      })}
      <div className="flex items-center gap-2 pt-2">
        <button
          onClick={save}
          className="rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90"
        >
          Save settings
        </button>
        {msg && <span className="text-sm text-teal-mint">{msg}</span>}
      </div>
    </div>
  );
}

function LibrariesTab() {
  const [libraries, setLibraries] = useState<LibraryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [adding, setAdding] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = () =>
    api.libraries().then(setLibraries).catch(() => setLibraries([])).finally(() => setLoading(false));

  useEffect(() => {
    load();
  }, []);

  const scan = async (id: number) => {
    try {
      await api.scanLibrary(id);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Scan failed");
    }
  };

  const remove = async (lib: LibraryDto) => {
    if (!confirm(`Delete library "${lib.name}"? This removes its catalog entries (files on disk are untouched).`))
      return;
    try {
      await api.deleteLibrary(lib.id);
      setLibraries((prev) => prev.filter((l) => l.id !== lib.id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete failed");
    }
  };

  if (loading) return <Spinner />;

  return (
    <div className="space-y-4">
      {error && <div className="rounded-xl bg-red-500/10 px-3 py-2 text-sm text-red-400">{error}</div>}

      <div className="flex justify-between">
        <p className="text-sm text-neutral-400">
          Configure libraries and their storage folders. Add a folder to a library when it spans more
          than one location (e.g. a NAS share that ran out of space).
        </p>
        <button
          onClick={() => setAdding(true)}
          className="shrink-0 rounded-xl bg-teal px-4 py-1.5 text-sm font-medium text-white hover:bg-teal/90"
        >
          + Add library
        </button>
      </div>

      <div className="space-y-3">
        {libraries.map((lib) => (
          <div key={lib.id} className="rounded-2xl border border-neutral-800 bg-neutral-900/40 p-4">
            <div className="flex items-start justify-between gap-3">
              <div>
                <div className="font-medium">{lib.name}</div>
                <div className="text-xs text-neutral-500">
                  {lib.storageKind === 1 ? "SMB / UNC" : "Local"} · {lib.seriesCount} series ·{" "}
                  {(lib.paths?.length ?? 0) || 1} folder{(lib.paths?.length ?? 1) === 1 ? "" : "s"}
                </div>
              </div>
              <div className="flex shrink-0 gap-2">
                <button
                  onClick={() => scan(lib.id)}
                  className="rounded-lg bg-neutral-800 px-3 py-1.5 text-sm text-neutral-200 hover:bg-neutral-700"
                >
                  Scan
                </button>
                <button
                  onClick={() => setEditId(lib.id)}
                  className="rounded-lg bg-neutral-800 px-3 py-1.5 text-sm text-neutral-200 hover:bg-neutral-700"
                >
                  Edit
                </button>
                <button
                  onClick={() => remove(lib)}
                  className="rounded-lg bg-red-500/10 px-3 py-1.5 text-sm text-red-400 hover:bg-red-500/20"
                >
                  Delete
                </button>
              </div>
            </div>
            <ul className="mt-3 space-y-1">
              {(lib.paths?.length ? lib.paths.map((p) => p.path) : [lib.rootPath]).map((p, i) => (
                <li key={i} className="truncate rounded-lg bg-neutral-800/60 px-3 py-1.5 text-sm text-neutral-300">
                  {p}
                </li>
              ))}
            </ul>
          </div>
        ))}
        {libraries.length === 0 && (
          <p className="text-sm text-neutral-500">No libraries yet. Add one to get started.</p>
        )}
      </div>

      {adding && (
        <AddLibraryDialog
          onClose={() => setAdding(false)}
          onCreated={() => {
            setAdding(false);
            load();
          }}
        />
      )}
      {editId !== null && (
        <EditLibraryDialog
          library={libraries.find((l) => l.id === editId)!}
          onClose={() => setEditId(null)}
          onSaved={() => {
            setEditId(null);
            load();
          }}
        />
      )}
    </div>
  );
}

function EditLibraryDialog({
  library,
  onClose,
  onSaved,
}: {
  library: LibraryDto;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(library.name);
  const [folderWatch, setFolderWatch] = useState(library.folderWatch);
  const [paths, setPaths] = useState<string[]>(
    library.paths?.length ? library.paths.map((p) => p.path) : [library.rootPath]
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [testMsg, setTestMsg] = useState<{ ok: boolean; message: string } | null>(null);

  const isSmb = library.storageKind === 1;
  const cleanPaths = paths.map((p) => p.trim()).filter(Boolean);

  const setPath = (i: number, value: string) =>
    setPaths((prev) => prev.map((p, idx) => (idx === i ? value : p)));

  const test = async () => {
    setBusy(true);
    setTestMsg(null);
    try {
      for (const p of cleanPaths) {
        const r = await api.testStorage({ storageKind: library.storageKind, rootPath: p });
        if (!r.success) {
          setTestMsg({ ok: false, message: `${p}: ${r.message}` });
          return;
        }
      }
      setTestMsg({ ok: true, message: "All folders are reachable." });
    } catch (err) {
      setTestMsg({ ok: false, message: err instanceof Error ? err.message : "Test failed" });
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.updateLibrary(library.id, { name, folderWatch, paths: cleanPaths });
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save library");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="w-full max-w-lg rounded-2xl bg-neutral-900 p-6 shadow-2xl ring-1 ring-white/10">
        <h2 className="mb-4 text-lg font-semibold">Edit library</h2>

        <div className="space-y-3">
          <label className="block">
            <span className="mb-1 block text-sm text-neutral-400">Name</span>
            <input className="input" value={name} onChange={(e) => setName(e.target.value)} />
          </label>

          <div className="space-y-2">
            <span className="block text-sm text-neutral-400">
              Folders {isSmb ? "(SMB / UNC)" : "(local)"}
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
                  onClick={() => setPaths((prev) => (prev.length === 1 ? prev : prev.filter((_, idx) => idx !== i)))}
                  disabled={paths.length === 1}
                  title="Remove folder"
                  className="rounded-lg bg-neutral-800 px-3 py-2 text-sm text-neutral-300 hover:bg-neutral-700 disabled:opacity-30"
                >
                  −
                </button>
              </div>
            ))}
            <button
              onClick={() => setPaths((prev) => [...prev, ""])}
              className="text-sm text-teal-mint hover:underline"
            >
              + Add another folder
            </button>
            <p className="text-xs text-neutral-500">
              New folders use this library's existing credentials. Removing a folder drops its content
              from the catalog on the next scan (files on disk are untouched).
            </p>
          </div>

          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-neutral-300">
            <input
              type="checkbox"
              checked={folderWatch}
              onChange={(e) => setFolderWatch(e.target.checked)}
              className="h-4 w-4 accent-teal"
            />
            Watch folders for changes
          </label>

          {testMsg && (
            <div
              className={`rounded-xl px-3 py-2 text-sm ${
                testMsg.ok ? "bg-teal/10 text-teal-mint" : "bg-red-500/10 text-red-400"
              }`}
            >
              {testMsg.message}
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
              onClick={save}
              disabled={busy || !name || cleanPaths.length === 0}
              className="rounded-xl bg-teal px-4 py-2 text-sm font-medium text-white hover:bg-teal/90 disabled:opacity-50"
            >
              Save
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = bytes / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(1)} ${units[i]}`;
}
