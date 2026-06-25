"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

// Phase 4.5 — Operator login. Reuses the existing /api/auth/login
// endpoint (External Auth → JWT cookie); operator vs admin role is
// decided by the JWT 'role' claim and enforced server-side by the
// OperatorOnly policy (Phase 4.2).
//
// Why a separate form file (not the admin LoginExperience):
//   - Tablet/phone-first layout — large hit targets, single column,
//     no marketing chrome.
//   - On success we route to /m/trips, not /home.
//   - Visual styling matches the dark operator shell.
export function OperatorLoginForm() {
  const router = useRouter();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username || !password || busy) return;
    setBusy(true);
    setError(null);
    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password }),
      });
      if (!res.ok) {
        const body = (await res.json().catch(() => null)) as { message?: string } | null;
        setError(body?.message ?? "Sign-in failed.");
        return;
      }
      router.replace("/m/trips");
      router.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Network error.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex min-h-dvh flex-col items-center justify-center px-6 py-12">
      <div className="mb-10 text-center">
        <div className="mx-auto mb-3 size-12 rounded-2xl bg-zinc-100 text-zinc-950 grid place-items-center text-xl font-semibold">
          D
        </div>
        <h1 className="text-2xl font-semibold">DTMS Operator</h1>
        <p className="mt-1 text-sm text-zinc-400">Sign in with your employee credentials.</p>
      </div>
      <form onSubmit={onSubmit} className="flex w-full max-w-sm flex-col gap-4">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="text-zinc-400">Employee code</span>
          <input
            type="text"
            inputMode="text"
            autoCapitalize="characters"
            autoCorrect="off"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            disabled={busy}
            className="h-12 rounded-xl border border-zinc-800 bg-zinc-900 px-4 text-base outline-none focus:border-zinc-500 disabled:opacity-50"
            required
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="text-zinc-400">Password</span>
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={busy}
            className="h-12 rounded-xl border border-zinc-800 bg-zinc-900 px-4 text-base outline-none focus:border-zinc-500 disabled:opacity-50"
            required
          />
        </label>
        {error ? (
          <div className="rounded-lg border border-red-900/60 bg-red-950/40 px-3 py-2 text-sm text-red-200">
            {error}
          </div>
        ) : null}
        <button
          type="submit"
          disabled={busy || !username || !password}
          className="mt-2 h-12 rounded-xl bg-zinc-100 text-base font-medium text-zinc-950 disabled:opacity-50"
        >
          {busy ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}
