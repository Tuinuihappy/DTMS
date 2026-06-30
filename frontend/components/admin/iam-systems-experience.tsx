"use client";

import { AlertCircle, KeyRound, Plus, Power, PowerOff, RefreshCw, X } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import {
  activateSystem,
  createSystem,
  deactivateSystem,
  listSystems,
  rotateCredential,
  type CreatedSystemResponse,
  type RotateCredentialResponse,
  type SystemSummaryDto,
} from "@/lib/api/iam-systems";
import { OneTimeSecretBanner } from "@/components/admin/one-time-secret-banner";
import { cn } from "@/lib/utils";

// Phase S.6 — federated source-system admin (SystemClient list + CRUD).
// Backend endpoints are under /api/v1/iam/systems (Phase S.4); this page
// is the operator-facing surface that replaces the psql + manual SHA256
// onboarding ritual. Detail page lives at /admin/systems/[key].

export function IamSystemsExperience() {
  const [items, setItems] = useState<SystemSummaryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Per-row busy tracking for activate/deactivate/rotate so individual
  // actions can show a spinner without blocking the whole table.
  const [busyKeys, setBusyKeys] = useState<Set<string>>(new Set());
  const [creating, setCreating] = useState(false);
  // The plaintext API key the backend returned on create / rotate.
  // Lives in state ONLY until the user dismisses the banner — once gone,
  // it's gone (matching the backend's "shown once" contract).
  const [secret, setSecret] = useState<
    | { kind: "created"; key: string; apiKey: string }
    | { kind: "rotated"; key: string; apiKey: string; rotatedAt: string }
    | null
  >(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setItems(await listSystems());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const markBusy = (key: string) =>
    setBusyKeys((prev) => {
      const next = new Set(prev);
      next.add(key);
      return next;
    });
  const clearBusy = (key: string) =>
    setBusyKeys((prev) => {
      const next = new Set(prev);
      next.delete(key);
      return next;
    });

  const handleActivate = async (key: string) => {
    markBusy(key);
    try {
      await activateSystem(key);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      clearBusy(key);
    }
  };

  const handleDeactivate = async (key: string) => {
    if (!confirm(`Deactivate system "${key}"? Inbound auth will start returning 401.`)) return;
    markBusy(key);
    try {
      await deactivateSystem(key);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      clearBusy(key);
    }
  };

  const handleRotate = async (key: string) => {
    if (!confirm(`Rotate the API key for "${key}"? The current key stops working immediately.`)) return;
    markBusy(key);
    try {
      const res: RotateCredentialResponse = await rotateCredential(key);
      setSecret({ kind: "rotated", key, apiKey: res.apiKey, rotatedAt: res.rotatedAt });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      clearBusy(key);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">Source systems</h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Federated systems that authenticate to DTMS via{" "}
            <code className="font-mono">/api/v1/source/&#123;key&#125;/*</code> and receive callbacks for
            orders they create. Permissions auto-seeded on create
            (<code className="font-mono">dtms:source:&#123;key&#125;:order:write/read</code>).
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={load}
            className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
          >
            <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
            Refresh
          </button>
          <button
            type="button"
            onClick={() => setCreating(true)}
            className="inline-flex items-center gap-1.5 rounded-full bg-[var(--color-ink-900)] px-3 py-1.5 text-[11.5px] font-semibold text-white hover:bg-[var(--color-ink-700)]"
          >
            <Plus className="h-3 w-3" strokeWidth={2.6} />
            New system
          </button>
        </div>
      </header>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700 dark:border-rose-500/30 dark:bg-rose-950/40 dark:text-rose-300">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
          <span>{error}</span>
          <button
            type="button"
            onClick={() => setError(null)}
            className="ml-auto text-rose-600 hover:text-rose-800 dark:text-rose-300 dark:hover:text-rose-100"
            aria-label="Dismiss error"
          >
            <X className="h-3 w-3" strokeWidth={2.4} />
          </button>
        </div>
      )}

      {secret && (
        <OneTimeSecretBanner
          title={
            secret.kind === "created"
              ? `API key for "${secret.key}" — copy now`
              : `New API key for "${secret.key}" — copy now (old key revoked)`
          }
          secret={secret.apiKey}
          helpText={
            secret.kind === "created"
              ? 'Send this key to the source-system operator. They use it as `Authorization: ApiKey <value>` on every inbound POST.'
              : `Rotated at ${secret.rotatedAt}. The previous key is no longer accepted.`
          }
          onDismiss={() => setSecret(null)}
        />
      )}

      <div className="overflow-hidden rounded-xl border border-[var(--color-ink-100)] bg-white/70 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <table className="w-full text-left text-[12.5px]">
          <thead className="bg-[var(--color-ink-50)] text-[10.5px] uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.04]">
            <tr>
              <th className="px-3 py-2 font-semibold">Key</th>
              <th className="px-3 py-2 font-semibold">Display name</th>
              <th className="px-3 py-2 font-semibold">Status</th>
              <th className="px-3 py-2 font-semibold">Owner</th>
              <th className="px-3 py-2 font-semibold">Created</th>
              <th className="px-3 py-2 font-semibold">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--color-ink-100)] dark:divide-white/[0.04]">
            {loading && items.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-[var(--color-ink-400)]">
                  Loading…
                </td>
              </tr>
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-3 py-6 text-center text-[var(--color-ink-400)]">
                  No systems yet. Click <em>New system</em> to onboard one.
                </td>
              </tr>
            ) : (
              items.map((s) => {
                const busy = busyKeys.has(s.key);
                return (
                  <tr key={s.key} className="hover:bg-[var(--color-ink-50)]/50 dark:hover:bg-white/[0.03]">
                    <td className="px-3 py-2 font-mono text-[12px]">
                      <Link
                        href={`/admin/systems/${encodeURIComponent(s.key)}`}
                        className="text-[var(--color-brand-600)] hover:underline"
                      >
                        {s.key}
                      </Link>
                    </td>
                    <td className="px-3 py-2 text-[var(--color-ink-700)]">{s.displayName}</td>
                    <td className="px-3 py-2">
                      <span
                        className={cn(
                          "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-medium",
                          s.isActive
                            ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300"
                            : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06]",
                        )}
                      >
                        {s.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-[var(--color-ink-500)]">{s.ownerContact ?? "—"}</td>
                    <td className="px-3 py-2 text-[11px] text-[var(--color-ink-500)]">
                      {new Date(s.createdAt).toLocaleDateString()}
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        {s.isActive ? (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => handleDeactivate(s.key)}
                            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] disabled:opacity-50 dark:bg-white/[0.05]"
                            title="Deactivate"
                          >
                            <PowerOff className="h-3 w-3" strokeWidth={2.2} />
                            Deactivate
                          </button>
                        ) : (
                          <button
                            type="button"
                            disabled={busy}
                            onClick={() => handleActivate(s.key)}
                            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] disabled:opacity-50 dark:bg-white/[0.05]"
                            title="Activate"
                          >
                            <Power className="h-3 w-3" strokeWidth={2.2} />
                            Activate
                          </button>
                        )}
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => handleRotate(s.key)}
                          className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] disabled:opacity-50 dark:bg-white/[0.05]"
                          title="Rotate API key"
                        >
                          <KeyRound className="h-3 w-3" strokeWidth={2.2} />
                          Rotate
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {creating && (
        <CreateSystemModal
          onClose={() => setCreating(false)}
          onCreated={(r) => {
            setCreating(false);
            setSecret({ kind: "created", key: r.key, apiKey: r.apiKey });
            void load();
          }}
        />
      )}
    </div>
  );
}

// ── Create modal ────────────────────────────────────────────────────────

function CreateSystemModal({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (response: CreatedSystemResponse) => void;
}) {
  const [key, setKey] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [ownerContact, setOwnerContact] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const valid = key.length > 0 && /^[a-z0-9-]{1,50}$/.test(key) && displayName.length > 0;

  const submit = async () => {
    if (!valid) return;
    setSubmitting(true);
    setError(null);
    try {
      const res = await createSystem({
        key,
        displayName,
        description: description || null,
        ownerContact: ownerContact || null,
        isActive: true,
      });
      onCreated(res);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl border border-[var(--color-ink-100)] bg-white p-5 shadow-xl dark:border-white/[0.06] dark:bg-[var(--color-ink-900)]">
        <div className="flex items-start justify-between">
          <h2 className="text-base font-semibold text-[var(--color-ink-900)] dark:text-white">New source system</h2>
          <button
            type="button"
            onClick={onClose}
            className="rounded p-1 text-[var(--color-ink-400)] hover:bg-[var(--color-ink-50)]"
            aria-label="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <p className="mt-1 text-[12px] text-[var(--color-ink-500)]">
          The API key will be shown once after create — copy it before closing the banner.
        </p>

        <div className="mt-4 space-y-3">
          <div>
            <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
              Key (slug)
            </label>
            <input
              value={key}
              onChange={(e) => setKey(e.target.value.toLowerCase())}
              placeholder="e.g. oms, sap, wms-acme"
              className="mt-1 w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
            />
            <p className="mt-1 text-[10.5px] text-[var(--color-ink-400)]">
              Lowercase letters, digits, and <code>-</code> only. Becomes part of URL +
              permission codes (<code>dtms:source:&#123;key&#125;:*</code>).
            </p>
          </div>
          <div>
            <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
              Display name
            </label>
            <input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="e.g. Delta OMS"
              className="mt-1 w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
            />
          </div>
          <div>
            <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
              Description (optional)
            </label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
              className="mt-1 w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
            />
          </div>
          <div>
            <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
              Owner contact (optional)
            </label>
            <input
              value={ownerContact}
              onChange={(e) => setOwnerContact(e.target.value)}
              placeholder="e.g. ops@delta.com"
              className="mt-1 w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
            />
          </div>
        </div>

        {error && (
          <div className="mt-3 flex items-start gap-2 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700 dark:border-rose-500/30 dark:bg-rose-950/40">
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
            <span>{error}</span>
          </div>
        )}

        <div className="mt-5 flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded border border-[var(--color-ink-200)] bg-white px-3 py-1.5 text-[12px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] dark:border-white/[0.08] dark:bg-white/[0.04]"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={!valid || submitting}
            onClick={submit}
            className="rounded bg-[var(--color-ink-900)] px-3 py-1.5 text-[12px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:cursor-not-allowed disabled:opacity-50"
          >
            {submitting ? "Creating…" : "Create system"}
          </button>
        </div>
      </div>
    </div>
  );
}
