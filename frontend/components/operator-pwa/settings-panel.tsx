"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { getMyProfile, type OperatorProfile } from "@/lib/api/operator";
import {
  checkPushSupport,
  getCurrentSubscription,
  sendTestNotification,
  subscribeToPush,
  unsubscribeFromPush,
} from "@/lib/operator-pwa/push";
import {
  clearQueue,
  drainQueue,
  getQueueDepth,
} from "@/lib/operator-pwa/offline-queue";

// Phase 4.5 — Operator settings:
//   - Profile summary (employeeCode, role, warehouse)
//   - Push enable/disable + test push
//   - Offline queue depth + sync now / clear
//   - Logout
type PushState =
  | { kind: "loading" }
  | { kind: "unsupported"; reason: string }
  | { kind: "off" }
  | { kind: "on"; endpoint: string };

export function SettingsPanel() {
  const router = useRouter();
  const [profile, setProfile] = useState<OperatorProfile | null>(null);
  const [pushState, setPushState] = useState<PushState>({ kind: "loading" });
  const [queueDepth, setQueueDepth] = useState(0);
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const refreshAll = async () => {
    const support = checkPushSupport();
    if (support.kind !== "supported") {
      setPushState({ kind: "unsupported", reason: support.reason });
    } else {
      const sub = await getCurrentSubscription();
      setPushState(sub ? { kind: "on", endpoint: sub.endpoint } : { kind: "off" });
    }
    try {
      setQueueDepth(await getQueueDepth());
    } catch {
      setQueueDepth(0);
    }
    try {
      setProfile(await getMyProfile());
    } catch {
      // Likely auth expired — let the next nav hit /m/login.
    }
  };

  useEffect(() => {
    refreshAll();
  }, []);

  const onEnablePush = async () => {
    setBusy(true);
    setNote(null);
    const label = navigator.userAgent.split(") ")[0]?.split(" (").pop() ?? "Unknown device";
    const result = await subscribeToPush(label);
    setBusy(false);
    if (result.ok) {
      setNote("Notifications enabled.");
      await refreshAll();
    } else {
      setNote(result.reason);
    }
  };

  const onDisablePush = async () => {
    setBusy(true);
    setNote(null);
    await unsubscribeFromPush();
    setBusy(false);
    setNote("Notifications disabled on this device.");
    await refreshAll();
  };

  const onTestPush = async () => {
    setBusy(true);
    setNote(null);
    try {
      await sendTestNotification();
      setNote("Test push fired — check your notification tray.");
    } catch (err) {
      setNote(err instanceof Error ? err.message : "Test push failed.");
    } finally {
      setBusy(false);
    }
  };

  const onSyncNow = async () => {
    setBusy(true);
    setNote(null);
    const result = await drainQueue();
    setBusy(false);
    setQueueDepth(result.remaining);
    setNote(
      result.lastError
        ? `${result.drained} synced, ${result.remaining} pending — ${result.lastError}`
        : `${result.drained} synced, ${result.remaining} pending.`,
    );
  };

  const onClearQueue = async () => {
    if (!window.confirm("Discard all pending offline actions? This cannot be undone.")) return;
    setBusy(true);
    await clearQueue();
    setQueueDepth(0);
    setBusy(false);
    setNote("Offline queue cleared.");
  };

  const onLogout = async () => {
    setBusy(true);
    await fetch("/api/auth/logout", { method: "POST" });
    router.replace("/m/login");
    router.refresh();
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      {note && (
        <div className="rounded-xl border border-zinc-800 bg-zinc-900/70 p-3 text-sm text-zinc-200">
          {note}
        </div>
      )}

      {profile && (
        <Card title="Profile">
          <Row label="Employee">{profile.employeeCode}</Row>
          <Row label="Name">{profile.displayName}</Row>
          <Row label="Role">{profile.role}</Row>
          <Row label="Warehouse">{profile.primaryWarehouseId ?? "Any"}</Row>
          {profile.currentTripId && (
            <Row label="Current trip">{profile.currentTripId.slice(0, 8)}</Row>
          )}
        </Card>
      )}

      <Card title="Notifications">
        {pushState.kind === "loading" && (
          <div className="text-sm text-zinc-500">Checking support…</div>
        )}
        {pushState.kind === "unsupported" && (
          <div className="text-sm text-zinc-400">{pushState.reason}</div>
        )}
        {pushState.kind === "off" && (
          <button
            onClick={onEnablePush}
            disabled={busy}
            className="h-11 rounded-xl bg-zinc-100 text-sm font-medium text-zinc-950 disabled:opacity-50"
          >
            Enable push notifications
          </button>
        )}
        {pushState.kind === "on" && (
          <div className="flex flex-col gap-2">
            <Row label="Status">
              <span className="text-emerald-300">Enabled on this device</span>
            </Row>
            <div className="grid grid-cols-2 gap-2">
              <button
                onClick={onTestPush}
                disabled={busy}
                className="h-11 rounded-xl border border-zinc-800 bg-zinc-900 text-sm text-zinc-200 disabled:opacity-50"
              >
                Send test push
              </button>
              <button
                onClick={onDisablePush}
                disabled={busy}
                className="h-11 rounded-xl border border-zinc-800 bg-zinc-900 text-sm text-zinc-300 disabled:opacity-50"
              >
                Disable
              </button>
            </div>
          </div>
        )}
      </Card>

      <Card title="Sync">
        <Row label="Pending offline actions">{queueDepth}</Row>
        <div className="grid grid-cols-2 gap-2">
          <button
            onClick={onSyncNow}
            disabled={busy}
            className="h-11 rounded-xl bg-zinc-100 text-sm font-medium text-zinc-950 disabled:opacity-50"
          >
            Sync now
          </button>
          <button
            onClick={onClearQueue}
            disabled={busy || queueDepth === 0}
            className="h-11 rounded-xl border border-zinc-800 bg-zinc-900 text-sm text-zinc-300 disabled:opacity-50"
          >
            Discard pending
          </button>
        </div>
      </Card>

      <button
        onClick={onLogout}
        disabled={busy}
        className="mt-4 h-12 rounded-xl border border-zinc-800 bg-zinc-900 text-sm font-medium text-zinc-200 disabled:opacity-50"
      >
        Log out
      </button>
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-zinc-800 bg-zinc-900/60 p-4">
      <h2 className="mb-3 text-xs font-medium uppercase tracking-wide text-zinc-500">
        {title}
      </h2>
      <div className="flex flex-col gap-3">{children}</div>
    </section>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 text-sm">
      <span className="text-zinc-500">{label}</span>
      <span className="truncate font-mono text-xs text-zinc-200">{children}</span>
    </div>
  );
}
