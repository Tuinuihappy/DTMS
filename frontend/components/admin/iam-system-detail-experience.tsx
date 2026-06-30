"use client";

import {
  AlertCircle,
  ArrowLeft,
  CheckCircle2,
  KeyRound,
  Pencil,
  Power,
  PowerOff,
  RefreshCw,
  Settings2,
  X,
  XCircle,
} from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import {
  activateSystem,
  deactivateSystem,
  getSystem,
  patchSystem,
  rotateCredential,
  setCallback,
  type CallbackConfigRequest,
  type CredentialSummary,
  type SystemDetailDto,
} from "@/lib/api/iam-systems";
import { OneTimeSecretBanner } from "@/components/admin/one-time-secret-banner";
import { cn } from "@/lib/utils";

// Phase S.6 — single-system detail page. Three cards: metadata,
// credential + rotate, callback config. Subscriptions live on a
// separate page (drill-down nav) — link out from the bottom of this
// page. Edit flows open inline modals styled like the create modal on
// the list page.

export function IamSystemDetailExperience({ systemKey }: { systemKey: string }) {
  const [data, setData] = useState<SystemDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [editingMetadata, setEditingMetadata] = useState(false);
  const [editingCallback, setEditingCallback] = useState(false);
  const [rotatedKey, setRotatedKey] = useState<{ apiKey: string; rotatedAt: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setData(await getSystem(systemKey));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [systemKey]);

  useEffect(() => {
    void load();
  }, [load]);

  const onActivate = async () => {
    setBusy(true);
    try {
      await activateSystem(systemKey);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onDeactivate = async () => {
    if (!confirm(`Deactivate "${systemKey}"? Inbound auth will return 401.`)) return;
    setBusy(true);
    try {
      await deactivateSystem(systemKey);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const onRotate = async () => {
    if (rotatedKey) {
      setError("A freshly rotated key is still visible above — copy it before rotating again.");
      return;
    }
    if (!confirm(
      `Rotate the API key for "${systemKey}"?\n\n` +
      `• The current key stops working the moment you click OK.\n` +
      `• A NEW key will be shown ONCE in a banner — copy it then test it.\n` +
      `• Do NOT rotate again until you have copied + verified the new key.`,
    )) return;
    setBusy(true);
    try {
      const r = await rotateCredential(systemKey);
      setRotatedKey({ apiKey: r.apiKey, rotatedAt: r.rotatedAt });
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  if (loading && !data) {
    return <div className="px-2 py-6 text-sm text-[var(--color-ink-400)]">Loading…</div>;
  }
  if (!data) {
    return (
      <div className="px-2 py-6">
        <p className="text-sm text-rose-600">{error ?? "System not found"}</p>
        <Link href="/admin/systems" className="mt-2 inline-block text-xs text-[var(--color-brand-600)] hover:underline">
          ← Back to systems
        </Link>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <Link
          href="/admin/systems"
          className="inline-flex items-center gap-1 text-[11.5px] text-[var(--color-ink-500)] hover:text-[var(--color-ink-700)]"
        >
          <ArrowLeft className="h-3 w-3" strokeWidth={2.2} />
          All systems
        </Link>
        <div className="flex items-end justify-between gap-3">
          <div>
            <div className="flex items-baseline gap-3">
              <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">{data.displayName}</h1>
              <code className="font-mono text-[12px] text-[var(--color-ink-500)]">{data.key}</code>
            </div>
            <div className="mt-1 flex items-center gap-3 text-[12px] text-[var(--color-ink-500)]">
              <span
                className={cn(
                  "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-medium",
                  data.isActive
                    ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300"
                    : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
                )}
              >
                {data.isActive ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />}
                {data.isActive ? "Active" : "Inactive"}
              </span>
              <span>Created {new Date(data.createdAt).toLocaleDateString()}</span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={load}
              disabled={busy || loading}
              className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white disabled:opacity-50 dark:bg-white/[0.05]"
            >
              <RefreshCw className={cn("h-3 w-3", (busy || loading) && "animate-spin")} strokeWidth={2.4} />
              Refresh
            </button>
            {data.isActive ? (
              <button
                type="button"
                disabled={busy}
                onClick={onDeactivate}
                className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] disabled:opacity-50"
              >
                <PowerOff className="h-3 w-3" strokeWidth={2.2} />
                Deactivate
              </button>
            ) : (
              <button
                type="button"
                disabled={busy}
                onClick={onActivate}
                className="inline-flex items-center gap-1 rounded-full border border-emerald-300 bg-emerald-50 px-3 py-1.5 text-[11.5px] font-medium text-emerald-700 hover:bg-emerald-100 disabled:opacity-50"
              >
                <Power className="h-3 w-3" strokeWidth={2.2} />
                Activate
              </button>
            )}
          </div>
        </div>
      </header>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
          <span>{error}</span>
          <button type="button" onClick={() => setError(null)} className="ml-auto" aria-label="Dismiss">
            <X className="h-3 w-3" />
          </button>
        </div>
      )}

      {rotatedKey && (
        <OneTimeSecretBanner
          title={`New API key for "${data.key}" — copy now (old key revoked)`}
          secret={rotatedKey.apiKey}
          testKeyForSystem={data.key}
          helpText={`Rotated at ${rotatedKey.rotatedAt}. Click "Test this key" to confirm it authenticates, then send to the operator. Do NOT click Rotate again until you've copied + tested.`}
          onDismiss={() => setRotatedKey(null)}
        />
      )}

      {/* ── Metadata card ───────────────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Metadata</h2>
          <button
            type="button"
            onClick={() => setEditingMetadata(true)}
            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            <Pencil className="h-3 w-3" strokeWidth={2.2} />
            Edit
          </button>
        </div>
        <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-[12px]">
          <DefRow label="Description" value={data.description ?? "—"} />
          <DefRow label="Owner contact" value={data.ownerContact ?? "—"} />
        </dl>
      </section>

      {/* ── Credential card ─────────────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Credential</h2>
          <button
            type="button"
            onClick={onRotate}
            disabled={busy || rotatedKey !== null}
            title={rotatedKey ? "Copy + test the visible key first before rotating again" : undefined}
            className="inline-flex items-center gap-1 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-[11px] text-amber-700 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <KeyRound className="h-3 w-3" strokeWidth={2.2} />
            Rotate key
          </button>
        </div>
        <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
          The current key works forever. Rotate <em>only</em> when the key
          was leaked, someone who held it left, or a policy mandates it —
          rotating revokes the existing key immediately and you must
          re-distribute the new one to the source system.
        </p>
        {data.credential ? (
          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-[12px]">
            <DefRow label="Auth scheme (inbound)" value={data.credential.authScheme} mono />
            <DefRow label="Last updated" value={new Date(data.credential.updatedAt).toLocaleString()} />
          </dl>
        ) : (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">No credential row.</p>
        )}
      </section>

      {/* ── Callback card ───────────────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Outbound callback</h2>
          <button
            type="button"
            onClick={() => setEditingCallback(true)}
            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            <Settings2 className="h-3 w-3" strokeWidth={2.2} />
            Configure
          </button>
        </div>
        {data.credential?.hasCallbackBaseUrl ? (
          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 text-[12px]">
            <DefRow label="Base URL" value={data.credential.callbackBaseUrl ?? "—"} mono />
            <DefRow label="Auth scheme" value={data.credential.callbackAuthScheme ?? "—"} mono />
            <DefRow label="Timeout (ms)" value={String(data.credential.callbackTimeoutMs)} />
          </dl>
        ) : (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">
            No callback URL set. DTMS will not call this system back when its orders complete.
          </p>
        )}
      </section>

      {/* ── Subscriptions summary + link to drill-down ──────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <div className="flex items-center justify-between">
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Event subscriptions</h2>
          <Link
            href={`/admin/systems/${encodeURIComponent(data.key)}/subscriptions`}
            className="inline-flex items-center gap-1 rounded border border-[var(--color-ink-100)] bg-white px-2 py-1 text-[11px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            Manage subscriptions →
          </Link>
        </div>
        {data.subscriptions.length === 0 ? (
          <p className="mt-3 text-[12px] text-[var(--color-ink-400)]">
            No subscriptions. DTMS will not fan out any events to this system until at least one is added.
          </p>
        ) : (
          <ul className="mt-3 space-y-1 text-[12px]">
            {data.subscriptions.map((s) => (
              <li key={s.eventType} className="flex items-center gap-2">
                <code className="font-mono text-[11.5px]">{s.eventType}</code>
                <span className="text-[var(--color-ink-400)]">→</span>
                <code className="font-mono text-[11.5px] text-[var(--color-ink-500)]">{s.payloadFormatKey}</code>
                <span
                  className={cn(
                    "ml-auto inline-flex rounded-full px-2 py-0.5 text-[10px] font-medium",
                    s.enabled
                      ? "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300"
                      : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)]",
                  )}
                >
                  {s.enabled ? "Enabled" : "Disabled"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* ── Permissions auto-seeded ─────────────────────────────────── */}
      <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Granted permissions</h2>
        <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
          Seeded automatically at create time from the S.3.1a template list. Custom grant/revoke
          is not yet wired in the UI — use psql if needed.
        </p>
        <ul className="mt-3 grid grid-cols-1 gap-1 text-[12px] md:grid-cols-2">
          {data.permissions.map((p) => (
            <li key={p}>
              <code className="font-mono text-[11.5px] text-[var(--color-ink-700)]">{p}</code>
            </li>
          ))}
        </ul>
      </section>

      {editingMetadata && (
        <EditMetadataModal
          initial={data}
          onClose={() => setEditingMetadata(false)}
          onSaved={() => {
            setEditingMetadata(false);
            void load();
          }}
        />
      )}
      {editingCallback && (
        <ConfigureCallbackModal
          systemKey={data.key}
          initial={data.credential}
          onClose={() => setEditingCallback(false)}
          onSaved={() => {
            setEditingCallback(false);
            void load();
          }}
        />
      )}
    </div>
  );
}

// ── Subcomponents ───────────────────────────────────────────────────────

function DefRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <>
      <dt className="text-[10.5px] uppercase tracking-[0.08em] text-[var(--color-ink-400)]">{label}</dt>
      <dd className={cn("text-[var(--color-ink-700)]", mono && "font-mono text-[11.5px]")}>{value}</dd>
    </>
  );
}

function EditMetadataModal({
  initial,
  onClose,
  onSaved,
}: {
  initial: SystemDetailDto;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [displayName, setDisplayName] = useState(initial.displayName);
  const [description, setDescription] = useState(initial.description ?? "");
  const [ownerContact, setOwnerContact] = useState(initial.ownerContact ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await patchSystem(initial.key, {
        displayName: displayName || null,
        description,
        ownerContact,
      });
      onSaved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ModalShell title="Edit metadata" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Display name">
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
        <Field label="Description">
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
        <Field label="Owner contact">
          <input
            value={ownerContact}
            onChange={(e) => setOwnerContact(e.target.value)}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
      </div>
      {error && <ErrorBanner message={error} />}
      <ModalFooter onCancel={onClose} onSave={submit} saving={submitting} />
    </ModalShell>
  );
}

function ConfigureCallbackModal({
  systemKey,
  initial,
  onClose,
  onSaved,
}: {
  systemKey: string;
  initial: CredentialSummary | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [callbackBaseUrl, setCallbackBaseUrl] = useState(initial?.callbackBaseUrl ?? "");
  const [callbackAuthScheme, setCallbackAuthScheme] = useState<string>(
    initial?.callbackAuthScheme ?? "",
  );
  const [callbackBearerToken, setCallbackBearerToken] = useState("");
  const [callbackTimeoutMs, setCallbackTimeoutMs] = useState(initial?.callbackTimeoutMs ?? 10000);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const body: CallbackConfigRequest = {
        callbackBaseUrl: callbackBaseUrl || null,
        callbackAuthScheme: callbackAuthScheme || null,
        callbackBearerToken: callbackBearerToken || null,
        callbackTimeoutMs,
      };
      await setCallback(systemKey, body);
      onSaved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ModalShell title="Configure outbound callback" onClose={onClose}>
      <p className="mb-3 text-[11.5px] text-[var(--color-ink-500)]">
        DTMS POSTs callback events to <code>&#123;baseUrl&#125;/events</code>. Bearer token is
        stored once + reused; leave token blank to keep current value.
      </p>
      <div className="space-y-3">
        <Field label="Callback base URL">
          <input
            value={callbackBaseUrl}
            onChange={(e) => setCallbackBaseUrl(e.target.value)}
            placeholder="https://oms.internal/dtms-callbacks"
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[12px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
        <Field label="Auth scheme">
          <select
            value={callbackAuthScheme}
            onChange={(e) => setCallbackAuthScheme(e.target.value)}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          >
            <option value="">— none —</option>
            <option value="bearer">Bearer token</option>
          </select>
        </Field>
        {callbackAuthScheme === "bearer" && (
          <Field label="Bearer token (sent on every callback)">
            <input
              value={callbackBearerToken}
              onChange={(e) => setCallbackBearerToken(e.target.value)}
              placeholder="Leave blank to keep current"
              className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[12px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
              type="password"
            />
          </Field>
        )}
        <Field label="Timeout (ms)">
          <input
            type="number"
            value={callbackTimeoutMs}
            onChange={(e) => setCallbackTimeoutMs(parseInt(e.target.value || "0", 10))}
            min={1000}
            max={60000}
            className="w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 text-[13px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </Field>
      </div>
      {error && <ErrorBanner message={error} />}
      <ModalFooter onCancel={onClose} onSave={submit} saving={submitting} />
    </ModalShell>
  );
}

function ModalShell({ title, children, onClose }: { title: string; children: React.ReactNode; onClose: () => void }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl border border-[var(--color-ink-100)] bg-white p-5 shadow-xl dark:border-white/[0.06] dark:bg-[var(--color-ink-900)]">
        <div className="flex items-start justify-between">
          <h2 className="text-base font-semibold text-[var(--color-ink-900)] dark:text-white">{title}</h2>
          <button type="button" onClick={onClose} className="rounded p-1 text-[var(--color-ink-400)] hover:bg-[var(--color-ink-50)]">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="mt-4">{children}</div>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
        {label}
      </label>
      <div className="mt-1">{children}</div>
    </div>
  );
}

function ErrorBanner({ message }: { message: string }) {
  return (
    <div className="mt-3 flex items-start gap-2 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700">
      <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
      <span>{message}</span>
    </div>
  );
}

function ModalFooter({ onCancel, onSave, saving }: { onCancel: () => void; onSave: () => void; saving: boolean }) {
  return (
    <div className="mt-5 flex items-center justify-end gap-2">
      <button
        type="button"
        onClick={onCancel}
        className="rounded border border-[var(--color-ink-200)] bg-white px-3 py-1.5 text-[12px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
      >
        Cancel
      </button>
      <button
        type="button"
        disabled={saving}
        onClick={onSave}
        className="rounded bg-[var(--color-ink-900)] px-3 py-1.5 text-[12px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:opacity-50"
      >
        {saving ? "Saving…" : "Save"}
      </button>
    </div>
  );
}
