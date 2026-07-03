"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { usePoolTrips } from "@/lib/hooks/use-pool-trips";
import { acknowledgeTrip } from "@/lib/api/operator";
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
  const { trips, loading, connected, refetch } = usePoolTrips();
  const [previewTripId, setPreviewTripId] = useState<string | null>(null);
  const [busyTripId, setBusyTripId] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const previewTrip = trips.find((t) => t.tripId === previewTripId) ?? null;

  // Auto-close the drawer if the trip disappears from the pool while
  // it was open (someone else claimed it, or an admin cancelled it).
  // The reducer already removed the row; we surface a toast so the
  // operator understands why the drawer just closed.
  useEffect(() => {
    if (previewTripId && !previewTrip) {
      setToast("Trip กว่าจะกด confirm ก็ถูก claim ไปแล้ว");
      window.setTimeout(() => setToast(null), 3_000);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [previewTripId, previewTrip]);

  const handleConfirmClaim = async () => {
    if (!previewTrip || busyTripId) return;
    const trip = previewTrip;
    setBusyTripId(trip.tripId);
    try {
      const result = await acknowledgeTrip(trip.tripId);
      if (result.delivered) {
        router.push(`/m/trips/${trip.tripId}`);
        return;
      }
      // Offline — the action is queued; nothing to navigate to yet.
      setToast("Queued — will retry when you're online");
      setPreviewTripId(null);
    } catch (err) {
      // enqueueAction throws on 4xx. 409 = another operator won the CAS.
      // The error message from the backend body is like {"error":"TRIP_ALREADY_CLAIMED"}
      // — we just show a friendly Thai toast + reconcile via refetch.
      const msg = err instanceof Error ? err.message : "Claim failed";
      const isConflict = msg.includes("TRIP_ALREADY_CLAIMED") || msg.includes("409");
      setToast(
        isConflict
          ? `คนอื่นรับ ${trip.orderRef || "trip"} ไปแล้ว`
          : `Claim failed: ${msg}`,
      );
      setPreviewTripId(null);
      await refetch();
    } finally {
      setBusyTripId(null);
      window.setTimeout(() => setToast(null), 3_000);
    }
  };

  return (
    <div className="flex flex-col">
      <PoolHeader connected={connected} count={trips.length} />
      {loading ? (
        <div className="p-6 text-sm text-zinc-500">Loading pool…</div>
      ) : trips.length === 0 ? (
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
  return (
    <>
      {/* Backdrop — tapping it counts as Cancel */}
      <button
        type="button"
        aria-label="Cancel"
        onClick={onCancel}
        className="fixed inset-0 z-40 bg-black/60"
      />
      <div className="fixed inset-x-0 bottom-0 z-50 rounded-t-3xl border-t border-zinc-800 bg-zinc-950 shadow-2xl">
        <div className="mx-auto w-full max-w-2xl px-6 pb-6 pt-4">
          <div className="mx-auto h-1 w-10 rounded-full bg-zinc-700" />
          <h2 className="mt-4 text-lg font-semibold text-zinc-100">
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
    </>
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
