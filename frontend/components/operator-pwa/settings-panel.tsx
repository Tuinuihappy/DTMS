"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { getMyProfile, type OperatorProfile } from "@/lib/api/operator";
import { ModalLayer } from "@/components/primitives/modal-layer";
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
import {
  isWakeLockSupported,
  keepAwakePref,
  playTone,
  soundPref,
  unlockAudio,
  vibrate,
} from "@/lib/operator-pwa/alert-output";

// Phase 4.5 — Operator settings:
//   - Profile summary (employeeCode, role, warehouse)
//   - New-trip alert sound on/off + test, keep-screen-awake
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
    let result: Awaited<ReturnType<typeof subscribeToPush>>;
    try {
      result = await subscribeToPush(label);
    } catch (err) {
      result = { ok: false, reason: err instanceof Error ? err.message : "Enabling push failed." };
    } finally {
      setBusy(false);
    }
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

  // Tablets are shared between shifts. The offline queue's rows carry no
  // owner, so whatever is left at logout would replay under the next
  // operator's cookie, and this device's push subscription would keep
  // delivering the previous operator's trips. So logout always clears both
  // — and first asks, when something is still unsent.
  const [unsentAtLogout, setUnsentAtLogout] = useState<number | null>(null);

  const signOut = async () => {
    setBusy(true);
    try {
      await unsubscribeFromPush();
    } catch {
      // No subscription, or push unsupported — nothing to undo.
    }
    try {
      await clearQueue();
    } catch {
      // IndexedDB unavailable — there was no queue to replay either.
    }
    await fetch("/api/auth/logout", { method: "POST" });
    router.replace("/m/login");
    router.refresh();
  };

  const onLogout = async () => {
    const depth = await getQueueDepth().catch(() => 0);
    if (depth > 0) {
      setUnsentAtLogout(depth);
      return;
    }
    await signOut();
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

      <AlertSettings />

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

      {unsentAtLogout !== null && (
        <LogoutWithUnsentDialog
          count={unsentAtLogout}
          onCancel={() => setUnsentAtLogout(null)}
          onSignOut={signOut}
        />
      )}
    </div>
  );
}

// Asked when Log out finds actions still in the offline queue. "Send
// first" drains and signs out only once the queue is empty; "Discard"
// signs out and the queue is cleared with everything else.
function LogoutWithUnsentDialog({
  count,
  onCancel,
  onSignOut,
}: {
  count: number;
  onCancel: () => void;
  onSignOut: () => Promise<void>;
}) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const [remaining, setRemaining] = useState(count);
  const [busy, setBusy] = useState<"send" | "discard" | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const sendThenSignOut = async () => {
    setBusy("send");
    setProblem(null);
    const result = await drainQueue();
    if (result.remaining === 0) {
      await onSignOut();
      return;
    }
    setRemaining(result.remaining);
    setProblem(result.lastError ?? "ยังส่งไม่ได้");
    setBusy(null);
  };

  const discardThenSignOut = async () => {
    setBusy("discard");
    await onSignOut();
  };

  return (
    <ModalLayer
      open
      onClose={onCancel}
      dismissible={busy === null}
      intent="irreversible"
      safeRef={cancelRef}
      backdropClassName="bg-black/60"
    >
      {({ panelProps, titleId, panelStyle }) => (
        <div
          {...panelProps}
          key="logout-unsent"
          style={panelStyle}
          className="fixed inset-x-0 bottom-0 rounded-t-3xl border-t border-zinc-800 bg-zinc-950 shadow-2xl outline-none"
        >
          <div className="mx-auto w-full max-w-2xl px-6 pb-6 pt-4">
            <div className="mx-auto h-1 w-10 rounded-full bg-zinc-700" />
            <h2 id={titleId} className="mt-4 text-lg font-semibold text-zinc-100">
              ยังมี {remaining} รายการที่ยังไม่ได้ส่ง
            </h2>
            <p className="mt-1 text-sm text-zinc-400">
              เช่น pickup หรือ drop ที่บันทึกตอน offline ถ้า logout ตอนนี้
              รายการเหล่านี้จะถูกลบออกจากเครื่อง และจะไม่ถูกส่งในชื่อของคุณ
            </p>
            {problem && (
              <div
                role="alert"
                className="mt-3 rounded-xl border border-amber-900/60 bg-amber-950/40 p-3 text-xs text-amber-200"
              >
                ส่งไม่สำเร็จ: {problem}
              </div>
            )}
            <div className="mt-5 flex flex-col gap-3">
              <button
                type="button"
                onClick={() => void sendThenSignOut()}
                disabled={busy !== null}
                className="h-12 rounded-2xl bg-cyan-500 text-sm font-semibold text-zinc-950 disabled:opacity-50"
              >
                {busy === "send" ? "กำลังส่ง…" : "ส่งก่อน แล้วค่อย logout"}
              </button>
              <button
                type="button"
                onClick={() => void discardThenSignOut()}
                disabled={busy !== null}
                className="h-12 rounded-2xl border border-rose-900/60 bg-rose-950/40 text-sm font-medium text-rose-200 disabled:opacity-50"
              >
                {busy === "discard" ? "กำลัง logout…" : "ทิ้งรายการ แล้ว logout"}
              </button>
              <button
                ref={cancelRef}
                type="button"
                onClick={onCancel}
                disabled={busy !== null}
                className="h-12 rounded-2xl border border-zinc-800 bg-zinc-900 text-sm font-medium text-zinc-300 disabled:opacity-50"
              >
                ยกเลิก
              </button>
            </div>
          </div>
        </div>
      )}
    </ModalLayer>
  );
}

// Per-device switches for the in-app alerts (OperatorAlertsProvider) and
// the screen wake lock (KeepAwake). Prefs live in localStorage, so they're
// read after mount only — reading during render would mismatch the server.
function AlertSettings() {
  const [ready, setReady] = useState(false);
  const [sound, setSound] = useState(true);
  const [awake, setAwake] = useState(true);
  const [awakeSupported, setAwakeSupported] = useState(true);

  useEffect(() => {
    setSound(soundPref.get());
    setAwake(keepAwakePref.get());
    setAwakeSupported(isWakeLockSupported());
    setReady(true);
  }, []);

  const onTest = async () => {
    // This tap is itself the user gesture that unlocks audio.
    await unlockAudio();
    void playTone("pool-new");
    vibrate("pool-new");
  };

  return (
    <Card title="การเตือนงานใหม่">
      <Toggle
        label="เสียงเตือนงานใหม่"
        hint="ปิดแล้วยังมีแถบเตือน แต่ไม่ดังและไม่สั่น"
        checked={sound}
        disabled={!ready}
        onChange={(on) => {
          soundPref.set(on);
          setSound(on);
        }}
      />
      <button
        onClick={onTest}
        className="h-11 rounded-xl border border-zinc-700 text-sm font-medium text-zinc-100 active:bg-zinc-800"
      >
        ทดสอบเสียง
      </button>
      <Toggle
        label="กันจอดับระหว่างเปิดแอป"
        hint={
          awakeSupported
            ? "จอไม่ดับเองระหว่างเปิดแอป · ใช้แบตมากขึ้น และเครื่องจะไม่ล็อกหน้าจอเอง"
            : "เครื่องนี้ไม่รองรับ (ต้องเปิดผ่าน https หรือตั้ง flag ของ Chrome)"
        }
        checked={awakeSupported && awake}
        disabled={!ready || !awakeSupported}
        onChange={(on) => {
          keepAwakePref.set(on);
          setAwake(on);
        }}
      />
      <p className="text-xs text-zinc-500">
        เตือนได้เมื่อเปิดแอปและจออยู่ · ไม่ต้องใช้อินเทอร์เน็ต · ถ้าเงียบ ให้เช็กเสียงสื่อ (media) ของเครื่อง
      </p>
    </Card>
  );
}

function Toggle({
  label,
  hint,
  checked,
  disabled,
  onChange,
}: {
  label: string;
  hint: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (on: boolean) => void;
}) {
  return (
    <label className={"flex items-start justify-between gap-3 " + (disabled ? "opacity-50" : "")}>
      <span className="flex flex-col gap-0.5">
        <span className="text-sm text-zinc-100">{label}</span>
        <span className="text-xs text-zinc-500">{hint}</span>
      </span>
      <input
        type="checkbox"
        role="switch"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        className="mt-0.5 h-6 w-11 shrink-0 cursor-pointer appearance-none rounded-full bg-zinc-700 transition-colors before:block before:size-5 before:translate-x-0.5 before:translate-y-0.5 before:rounded-full before:bg-white before:transition-transform checked:bg-emerald-500 checked:before:translate-x-[22px] disabled:cursor-not-allowed"
      />
    </label>
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
