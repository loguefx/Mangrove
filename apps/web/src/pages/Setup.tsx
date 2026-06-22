import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth";
import { MangroveIcon } from "../components/Brand";

export default function Setup() {
  const { registerFirst } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (password !== confirm) {
      setError("Passwords do not match.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await registerFirst(username.trim(), email.trim() || null, password);
      navigate("/", { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Setup failed");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-full items-center justify-center bg-gradient-to-b from-neutral-950 to-teal-deep p-6">
      <div className="w-full max-w-md">
        <div className="mb-8 flex flex-col items-center text-center">
          <MangroveIcon className="mb-4 h-16 w-16 rounded-2xl shadow-lg" />
          <h1 className="text-2xl font-semibold text-white">Welcome to Mangrove</h1>
          <p className="mt-2 text-sm text-neutral-300">
            Create the first administrator account to get started. There is no default password.
          </p>
        </div>

        <form
          onSubmit={submit}
          className="space-y-4 rounded-2xl bg-neutral-900/80 p-6 shadow-xl ring-1 ring-white/10"
        >
          <Field label="Username">
            <input
              className="input"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoFocus
            />
          </Field>
          <Field label="Email (optional)">
            <input className="input" value={email} onChange={(e) => setEmail(e.target.value)} />
          </Field>
          <Field label="Password">
            <input
              type="password"
              className="input"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>
          <Field label="Confirm password">
            <input
              type="password"
              className="input"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
            />
          </Field>

          {error && <p className="text-sm text-red-400">{error}</p>}

          <button
            type="submit"
            disabled={busy || !username || password.length < 6}
            className="w-full rounded-xl bg-teal py-2.5 font-medium text-white transition hover:bg-teal/90 disabled:opacity-50"
          >
            {busy ? "Creating…" : "Create admin & continue"}
          </button>
        </form>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1 block text-sm font-medium text-neutral-300">{label}</label>
      {children}
    </div>
  );
}
