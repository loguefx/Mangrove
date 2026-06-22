import { useEffect, useState } from "react";
import {
  api,
  type AdminUserDto,
  type LibraryDto,
  type ServerStatsDto,
  type SettingDto,
  type TaskLogDto,
} from "../api";
import { PageHeader } from "../components/SeriesGrid";
import { Spinner } from "../components/Spinner";

const TABS = ["Users", "Tasks", "Stats", "Settings"] as const;
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

function SettingsTab() {
  const [settings, setSettings] = useState<SettingDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState<string | null>(null);

  useEffect(() => {
    api.settings().then(setSettings).catch(() => setSettings([])).finally(() => setLoading(false));
  }, []);

  const save = async () => {
    await api.saveSettings(settings);
    setMsg("Saved");
    setTimeout(() => setMsg(null), 1500);
  };

  if (loading) return <Spinner />;

  return (
    <div className="space-y-3">
      {settings.map((s, i) => (
        <div key={s.key} className="flex items-center gap-3">
          <label className="w-48 text-sm text-neutral-400">{s.key}</label>
          <input
            value={s.value ?? ""}
            onChange={(e) => {
              const next = [...settings];
              next[i] = { ...s, value: e.target.value };
              setSettings(next);
            }}
            className="input flex-1"
          />
        </div>
      ))}
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
