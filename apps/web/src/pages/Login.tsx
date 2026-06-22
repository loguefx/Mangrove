import { useState } from "react";
import { useAuth } from "../auth";
import { MangroveIcon } from "../components/Brand";

export default function Login() {
  const { login } = useAuth();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Login failed");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-full items-center justify-center bg-gradient-to-b from-neutral-950 to-teal-deep p-6">
      <div className="w-full max-w-sm">
        <div className="mb-8 flex flex-col items-center">
          <MangroveIcon className="mb-4 h-20 w-20 rounded-2xl shadow-lg shadow-teal-deep/40" />
          <h1 className="text-2xl font-semibold tracking-tight text-white">Mangrove</h1>
          <p className="mt-1 text-sm text-teal-mint/80">Your whole library, rooted in one place.</p>
        </div>

        <form
          onSubmit={submit}
          className="space-y-4 rounded-2xl bg-neutral-900/80 p-6 shadow-xl ring-1 ring-white/10 backdrop-blur"
        >
          <div>
            <label className="mb-1 block text-sm font-medium text-neutral-300">Username</label>
            <input
              className="w-full rounded-xl border border-neutral-700 bg-neutral-800 px-3 py-2 text-neutral-100 outline-none focus:border-teal-mint focus:ring-1 focus:ring-teal-mint"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoFocus
              autoComplete="username"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-neutral-300">Password</label>
            <input
              type="password"
              className="w-full rounded-xl border border-neutral-700 bg-neutral-800 px-3 py-2 text-neutral-100 outline-none focus:border-teal-mint focus:ring-1 focus:ring-teal-mint"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
            />
          </div>

          {error && <p className="text-sm text-red-400">{error}</p>}

          <button
            type="submit"
            disabled={busy || !username || !password}
            className="w-full rounded-xl bg-teal py-2.5 font-medium text-white transition hover:bg-teal/90 disabled:opacity-50"
          >
            {busy ? "Signing in…" : "Sign in"}
          </button>
        </form>
      </div>
    </div>
  );
}
