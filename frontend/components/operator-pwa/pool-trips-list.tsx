"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { ModalLayer } from "@/components/primitives/modal-layer";
import { usePoolTrips } from "@/lib/hooks/use-pool-trips";
import { claimPoolTrip, PoolClaimError } from "@/lib/api/operator";
import type { PoolTrip } from "@/lib/api/operator-pool";

// WMS PR-4b (PR-D) — Operator pool list. Cards are sorted FIFO by
// dispatchedAt (oldest first — fairness) so operators self-load the
// backlog.
//
// Two-step interaction: tap a card → a preview drawer slides up with
// the trip's headline details + an explicit "Acknowledge & Start"
// button. Confirming fires the atomic CAS + navigates to the trip
// detail on 204; a 409 (someone else won the race, or the trip left
// the pool while the drawer was open) surfaces as a toast + refetch
// to reconcile the local list.
export function PoolTripsList() {
  const router = useRouter();
  const { trips, loading, error, connected, refetch } = usePoolTrips();
  const [previewTripId, setPreviewTripId] = useState<string | null>(null);
  const [busyTripId, setBusyTripId] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const previewTrip = trips.find((t) => t.tripId === previewTripId) ?? null;

  // Re-read the pool each time this page opens. The provider's live feed
  // can't be trusted alone: trips are dispatched in the outbox worker, and
  // its PoolTripAdded only reaches operators connected to the API when the
  // SignalR Redis backplane is on (SignalR__UseRedisBackplane).
  useEffect(() => {
    void refetch();
  }, [refetch]);

  // Auto-close the drawer if the trip disappears from the pool while
  // it was open (someone else claimed it, or it was cancelled — the server
  // sends PoolTripRemoved for that). The reducer already removed the row;
  // we surface a toast so the operator understands why the drawer closed.
  useEffect(() => {
    if (previewTripId && !previewTrip) {
      setToast("งานนี้ไม่อยู่ใน Pool แล้ว — มีคนรับไปหรือถูกยกเลิก");
      window.setTimeout(() => setToast(null), 3_000);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewTripId, previewTrip]);

  const handleConfirmClaim = async () => {
    if (!previewTrip || busyTripId) return;
    const trip = previewTrip;
    setBusyTripId(trip.tripId);
    try {
      // Online only — see claimPoolTrip for why a claim is never queued.
      await claimPoolTrip(trip.tripId);
      router.push(`/m/trips/${trip.tripId}`);
      return;
    } catch (err) {
      const kind = err instanceof PoolClaimError ? err.kind : "failed";
      const msg = err instanceof Error ? err.message : "Claim failed";
      setToast(
        kind === "conflict"
          ? `คนอื่นรับ ${trip.orderRef || "trip"} ไปแล้ว`
          : kind === "offline"
            ? "ต้องออนไลน์เพื่อรับงาน — ยังไม่ได้รับงานนี้"
            : `Claim failed: ${msg}`,
      );
      // Offline keeps the drawer: the operator can retry once back online.
      if (kind !== "offline") setPreviewTripId(null);
      await refetch();
    } finally {
      setBusyTripId(null);
      window.setTimeout(() => setToast(null), 3_000);
    }
  };

  return (
    <div className="flex flex-col">
      <PoolHeader connected={connected} count={trips.length} />
      {/* A failed load must not read as "no work": with nothing to show it
          replaces the empty state; with an older list it warns above it. */}
      {error && !loading && (
        <PoolLoadError hasTrips={trips.length > 0} onRetry={refetch} />
      )}
      {loading ? (
        <div className="p-6 text-sm text-zinc-500">Loading pool…</div>
      ) : error && trips.length === 0 ? null : trips.length === 0 ? (
        <div className="m-4 rounded-xl border border-zinc-800 bg-zinc-900/50 p-6 text-center text-sm text-zinc-400">
          Pool is empty — new trips arrive here.
        </div>
      ) : (
        <ul className="flex flex-col gap-3 px-4 py-4">
          {trips.map((t) => (
            <li key={t.tripId}>
              <PoolCard
                trip={t}
                onPreview={() => setPreviewTripId(t.tripId)}
              />
            </li>
          ))}
        </ul>
      )}
      {previewTrip && (
        <ConfirmClaimDrawer
          trip={previewTrip}
          busy={busyTripId === previewTrip.tripId}
          onCancel={() => setPreviewTripId(null)}
          onConfirm={handleConfirmClaim}
        />
      )}
      {toast && (
        <div className="fixed inset-x-4 bottom-6 z-50 rounded-xl border border-amber-900/60 bg-amber-950/80 p-3 text-sm text-amber-100 shadow-lg">
          {toast}
        </div>
      )}
    </div>
  );
}

function PoolLoadError({
  hasTrips,
  onRetry,
}: {
  hasTrips: boolean;
  onRetry: () => Promise<void>;
}) {
  const [retrying, setRetrying] = useState(false);
  const retry = async () => {
    setRetrying(true);
    try {
      await onRetry();
    } finally {
      setRetrying(false);
    }
  };
  return (
    <div
      role="alert"
      className={`mx-4 mt-4 flex items-center justify-between gap-3 rounded-xl border p-3 text-sm ${
        hasTrips
          ? "border-amber-900/60 bg-amber-950/40 text-amber-200"
          : "border-rose-900/60 bg-rose-950/40 text-rose-200"
      }`}
    >
      <div>
        <div className="font-medium">โหลด Pool ไม่สำเร็จ</div>
        <div className="mt-0.5 text-xs opacity-80">
          {hasTrips
            ? "รายการด้านล่างอาจไม่ใช่ข้อมูลล่าสุด"
            : "ยังไม่รู้ว่ามีงานรออยู่หรือไม่ — ตรวจ Wi-Fi แล้วลองใหม่"}
        </div>
      </div>
      <button
        type="button"
        onClick={() => void retry()}
        disabled={retrying}
        className="min-h-11 shrink-0 rounded-lg bg-zinc-100 px-4 text-sm font-medium text-zinc-900 disabled:opacity-50"
      >
        {retrying ? "กำลังโหลด…" : "ลองใหม่"}
      </button>
    </div>
  );
}

function PoolHeader({ connected, count }: { connected: boolean; count: number }) {
  return (
    <div className="flex items-center justify-between border-b border-zinc-900 px-4 py-3 text-xs">
      <div className="text-zinc-400">
        <span className="font-medium text-zinc-200">{count}</span> trip{count === 1 ? "" : "s"} in pool
      </div>
      <div className="flex items-center gap-1.5">
        <span
          className={`inline-block h-2 w-2 rounded-full ${
            connected ? "bg-emerald-500" : "bg-amber-500"
          }`}
        />
        <span className="text-zinc-500">
          {connected ? "Live" : "Reconnecting…"}
        </span>
      </div>
    </div>
  );
}

function PoolCard({
  trip,
  onPreview,
}: {
  trip: PoolTrip;
  onPreview: () => void;
}) {
  const waited = formatWaited(trip.dispatchedAt);
  return (
    <button
      type="button"
      onClick={onPreview}
      className="w-full rounded-2xl border border-zinc-800 bg-zinc-900/60 p-4 text-left transition-transform active:scale-[0.99]"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-xs uppercase tracking-wide text-zinc-500">
            Order
          </div>
          <div className="mt-1 font-mono text-sm text-zinc-100">
            {trip.orderRef || trip.tripId.slice(0, 8)}
          </div>
        </div>
        <span className="rounded-full bg-cyan-500/15 px-2.5 py-1 text-xs font-medium text-cyan-300">
          {waited}
        </span>
      </div>
      <div className="mt-3 grid grid-cols-[1fr_auto_1fr] items-center gap-2 text-xs text-zinc-300">
        <div className="truncate rounded-md bg-zinc-950 px-2 py-1.5 font-mono text-[11px]">
          {trip.pickupCode || "—"}
        </div>
        <span className="text-zinc-600">→</span>
        <div className="truncate rounded-md bg-zinc-950 px-2 py-1.5 font-mono text-[11px]">
          {trip.dropCode || "—"}
        </div>
      </div>
      <div className="mt-3 flex items-center justify-between text-xs text-zinc-500">
        <div>
          {trip.itemCount} item{trip.itemCount === 1 ? "" : "s"}
          {trip.totalWeightKg > 0 ? ` · ${trip.totalWeightKg.toFixed(1)} kg` : ""}
        </div>
        <div className="font-medium text-cyan-300">Tap to preview →</div>
      </div>
    </button>
  );
}

// Bottom-sheet confirmation. Renders the trip's key details in a taller,
// more legible layout than the card and gates the claim behind an
// explicit "Acknowledge & Start" button. Cancel returns to the pool
// without any side effect (no CAS attempted, no broadcast fired).
function ConfirmClaimDrawer({
  trip,
  busy,
  onCancel,
  onConfirm,
}: {
  trip: PoolTrip;
  busy: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const waited = formatWaited(trip.dispatchedAt);
  // Claiming binds the trip to this operator: focus opens on Cancel.
  // Tapping the backdrop still counts as Cancel.
  const cancelRef = useRef<HTMLButtonElement>(null);
  return (
    <ModalLayer
      open
      onClose={onCancel}
      dismissible={!busy}
      intent="irreversible"
      safeRef={cancelRef}
      backdropClassName="bg-black/60"
    >
      {({ panelProps, titleId, panelStyle }) => (
      <div
        {...panelProps}
        key="confirm-claim"
        style={panelStyle}
        className="fixed inset-x-0 bottom-0 rounded-t-3xl border-t border-zinc-800 bg-zinc-950 shadow-2xl outline-none"
      >
        <div className="mx-auto w-full max-w-2xl px-6 pb-6 pt-4">
          <div className="mx-auto h-1 w-10 rounded-full bg-zinc-700" />
          <h2 id={titleId} className="mt-4 text-lg font-semibold text-zinc-100">
            Confirm to accept this trip
          </h2>
          <p className="mt-1 text-xs text-zinc-500">
            Once you acknowledge, the trip is bound to your account and other
            operators can no longer claim it.
          </p>

          <div className="mt-5 space-y-3 rounded-2xl border border-zinc-900 bg-zinc-900/40 p-4">
            <Row label="Order" value={trip.orderRef || trip.tripId.slice(0, 8)} mono />
            <Row label="Pickup" value={trip.pickupCode || "—"} mono />
            <Row label="Drop" value={trip.dropCode || "—"} mono />
            <Row
              label="Items"
              value={
                `${trip.itemCount} item${trip.itemCount === 1 ? "" : "s"}` +
                (trip.totalWeightKg > 0
                  ? ` · ${trip.totalWeightKg.toFixed(1)} kg`
                  : "")
              }
            />
            <Row label="Waited" value={waited} />
          </div>

          <div className="mt-5 grid grid-cols-2 gap-3">
            <button
              ref={cancelRef}
              type="button"
              onClick={onCancel}
              disabled={busy}
              className="rounded-2xl border border-zinc-800 bg-zinc-900 py-3 text-sm font-medium text-zinc-300 disabled:opacity-50"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={onConfirm}
              disabled={busy}
              className="rounded-2xl bg-cyan-500 py-3 text-sm font-semibold text-zinc-950 disabled:opacity-50"
            >
              {busy ? "Acknowledging…" : "Acknowledge & Start"}
            </button>
          </div>
        </div>
      </div>
      )}
    </ModalLayer>
  );
}

function Row({
  label,
  value,
  mono = false,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <div className="text-xs uppercase tracking-wide text-zinc-500">{label}</div>
      <div
        className={
          "truncate text-sm text-zinc-100" + (mono ? " font-mono text-[13px]" : "")
        }
      >
        {value}
      </div>
    </div>
  );
}

function formatWaited(dispatchedAt: string): string {
  const seconds = Math.max(0, Math.floor((Date.now() - Date.parse(dispatchedAt)) / 1000));
  if (seconds < 60) return `${seconds}s`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  return `${hours}h`;
}
