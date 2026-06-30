"use client";

import { AlertCircle, ArrowLeft, Plus, RefreshCw, Trash2, X } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import {
  createSubscription,
  deleteSubscription,
  getEventTypes,
  listSubscriptions,
  patchSubscription,
  type SubscriptionDto,
} from "@/lib/api/iam-systems";
import { cn } from "@/lib/utils";

// Phase S.6 — per-system subscription list with full CRUD. Drill-down
// page from /admin/systems/[key]. Backend at S.3.1b endpoints; this is
// the only surface that mutates iam.SystemEventSubscriptions outside
// of psql.

export function IamSystemSubscriptionsExperience({ systemKey }: { systemKey: string }) {
  const [items, setItems] = useState<SubscriptionDto[]>([]);
  const [eventTypes, setEventTypes] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [busyKeys, setBusyKeys] = useState<Set<string>>(new Set());

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [subs, types] = await Promise.all([listSubscriptions(systemKey), getEventTypes()]);
      setItems(subs);
      setEventTypes(types);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [systemKey]);

  useEffect(() => {
    void load();
  }, [load]);

  const markBusy = (k: string) =>
    setBusyKeys((prev) => {
      const next = new Set(prev);
      next.add(k);
      return next;
    });
  const clearBusy = (k: string) =>
    setBusyKeys((prev) => {
      const next = new Set(prev);
      next.delete(k);
      return next;
    });

  const onToggleEnabled = async (eventType: string, enabled: boolean) => {
    markBusy(eventType);
    try {
      await patchSubscription(systemKey, eventType, { enabled });
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      clearBusy(eventType);
    }
  };

  const onDelete = async (eventType: string) => {
    if (!confirm(`Delete subscription for "${eventType}"? Past callbacks already in the outbox will still dispatch; new events stop fanning out to this system.`)) return;
    markBusy(eventType);
    try {
      await deleteSubscription(systemKey, eventType);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      clearBusy(eventType);
    }
  };

  // Event types the system isn't already subscribed to — only these can be added.
  const subscribedSet = new Set(items.map((i) => i.eventType));
  const availableEventTypes = eventTypes.filter((t) => !subscribedSet.has(t));

  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <Link
          href={`/admin/systems/${encodeURIComponent(systemKey)}`}
          className="inline-flex items-center gap-1 text-[11.5px] text-[var(--color-ink-500)] hover:text-[var(--color-ink-700)]"
        >
          <ArrowLeft className="h-3 w-3" strokeWidth={2.2} />
          Back to {systemKey}
        </Link>
        <div className="flex items-end justify-between gap-3">
          <div>
            <h1 className="text-[1.55rem] font-semibold text-[var(--color-ink-900)]">
              Subscriptions · <code className="font-mono text-[1.1rem]">{systemKey}</code>
            </h1>
            <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
              DTMS fans out only the event types this system explicitly subscribes to. Each entry
              names the wire schema (payload formatter key) the callback POST uses.
            </p>
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={load}
              className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white dark:bg-white/[0.05]"
            >
              <RefreshCw className={cn("h-3 w-3", loading && "animate-spin")} strokeWidth={2.4} />
              Refresh
            </button>
            <button
              type="button"
              onClick={() => setCreating(true)}
              disabled={availableEventTypes.length === 0}
              className="inline-flex items-center gap-1 rounded-full bg-[var(--color-ink-900)] px-3 py-1.5 text-[11.5px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:opacity-50"
              title={availableEventTypes.length === 0 ? "Already subscribed to every event type" : "Add subscription"}
            >
              <Plus className="h-3 w-3" strokeWidth={2.6} />
              Add subscription
            </button>
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

      <div className="overflow-hidden rounded-xl border border-[var(--color-ink-100)] bg-white/70 dark:border-white/[0.06] dark:bg-white/[0.03]">
        <table className="w-full text-left text-[12.5px]">
          <thead className="bg-[var(--color-ink-50)] text-[10.5px] uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.04]">
            <tr>
              <th className="px-3 py-2 font-semibold">Event type</th>
              <th className="px-3 py-2 font-semibold">Payload format key</th>
              <th className="px-3 py-2 font-semibold">Status</th>
              <th className="px-3 py-2 font-semibold">Updated</th>
              <th className="px-3 py-2 font-semibold">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[var(--color-ink-100)] dark:divide-white/[0.04]">
            {loading && items.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-3 py-6 text-center text-[var(--color-ink-400)]">
                  Loading…
                </td>
              </tr>
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-3 py-6 text-center text-[var(--color-ink-400)]">
                  No subscriptions yet. Click <em>Add subscription</em> to wire one.
                </td>
              </tr>
            ) : (
              items.map((s) => {
                const busy = busyKeys.has(s.eventType);
                return (
                  <tr key={s.id} className="hover:bg-[var(--color-ink-50)]/50 dark:hover:bg-white/[0.03]">
                    <td className="px-3 py-2 font-mono text-[12px]">{s.eventType}</td>
                    <td className="px-3 py-2 font-mono text-[12px] text-[var(--color-ink-500)]">
                      {s.payloadFormatKey}
                    </td>
                    <td className="px-3 py-2">
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => onToggleEnabled(s.eventType, !s.enabled)}
                        className={cn(
                          "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-medium",
                          s.enabled
                            ? "bg-emerald-100 text-emerald-800 hover:bg-emerald-200 dark:bg-emerald-900/40 dark:text-emerald-300"
                            : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] hover:bg-[var(--color-ink-200)]",
                          busy && "opacity-50",
                        )}
                        title={s.enabled ? "Click to disable" : "Click to enable"}
                      >
                        {s.enabled ? "Enabled" : "Disabled"}
                      </button>
                    </td>
                    <td className="px-3 py-2 text-[11px] text-[var(--color-ink-500)]">
                      {new Date(s.updatedAtUtc).toLocaleString()}
                    </td>
                    <td className="px-3 py-2">
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => onDelete(s.eventType)}
                        className="inline-flex items-center gap-1 rounded border border-rose-300 bg-white px-2 py-1 text-[11px] text-rose-600 hover:bg-rose-50 disabled:opacity-50 dark:bg-white/[0.04]"
                      >
                        <Trash2 className="h-3 w-3" strokeWidth={2.2} />
                        Delete
                      </button>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {creating && (
        <AddSubscriptionModal
          systemKey={systemKey}
          availableEventTypes={availableEventTypes}
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false);
            void load();
          }}
        />
      )}
    </div>
  );
}

function AddSubscriptionModal({
  systemKey,
  availableEventTypes,
  onClose,
  onCreated,
}: {
  systemKey: string;
  availableEventTypes: string[];
  onClose: () => void;
  onCreated: () => void;
}) {
  const [eventType, setEventType] = useState(availableEventTypes[0] ?? "");
  const [payloadFormatKey, setPayloadFormatKey] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      await createSubscription(systemKey, { eventType, payloadFormatKey, enabled: true });
      onCreated();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSubmitting(false);
    }
  };

  const valid = eventType.length > 0 && payloadFormatKey.length > 0;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
      <div className="w-full max-w-md rounded-xl border border-[var(--color-ink-100)] bg-white p-5 shadow-xl dark:border-white/[0.06] dark:bg-[var(--color-ink-900)]">
        <div className="flex items-start justify-between">
          <h2 className="text-base font-semibold text-[var(--color-ink-900)] dark:text-white">Add subscription</h2>
          <button type="button" onClick={onClose} className="rounded p-1 text-[var(--color-ink-400)] hover:bg-[var(--color-ink-50)]">
            <X className="h-4 w-4" />
          </button>
        </div>
        <p className="mt-1 text-[12px] text-[var(--color-ink-500)]">
          The payload formatter key must match a DI-registered formatter on the backend
          (e.g. <code>oms.shipment.v1</code>). Add a new formatter class in
          Iam.Infrastructure if the system needs a custom wire shape.
        </p>

        <div className="mt-4 space-y-3">
          <div>
            <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
              Event type
            </label>
            <select
              value={eventType}
              onChange={(e) => setEventType(e.target.value)}
              className="mt-1 w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[12.5px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
            >
              {availableEventTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="block text-[11px] font-medium uppercase tracking-[0.06em] text-[var(--color-ink-500)]">
              Payload format key
            </label>
            <input
              value={payloadFormatKey}
              onChange={(e) => setPayloadFormatKey(e.target.value)}
              placeholder="e.g. oms.shipment.v1"
              className="mt-1 w-full rounded border border-[var(--color-ink-200)] bg-white px-3 py-2 font-mono text-[12.5px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
            />
          </div>
        </div>

        {error && (
          <div className="mt-3 flex items-start gap-2 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-[12px] text-rose-700">
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 flex-shrink-0" strokeWidth={2.2} />
            <span>{error}</span>
          </div>
        )}

        <div className="mt-5 flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded border border-[var(--color-ink-200)] bg-white px-3 py-1.5 text-[12px] text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)]"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={!valid || submitting}
            onClick={submit}
            className="rounded bg-[var(--color-ink-900)] px-3 py-1.5 text-[12px] font-semibold text-white hover:bg-[var(--color-ink-700)] disabled:opacity-50"
          >
            {submitting ? "Adding…" : "Add subscription"}
          </button>
        </div>
      </div>
    </div>
  );
}
