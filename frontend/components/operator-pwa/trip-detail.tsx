"use client";

import { useRouter } from "next/navigation";
import { useCallback, useState } from "react";
import {
  acknowledgeTrip,
  completeTrip,
  getAssignedTrips,
  recordDrop,
  recordPickup,
  tripEndedLabel,
  type AssignedTrip,
} from "@/lib/api/operator";
import { usePollSchedule } from "@/lib/hooks/use-poll-schedule";
import { tryGetCurrentPosition } from "@/lib/operator-pwa/geolocation";
import { PodCapture } from "./pod-capture";
import { TripRoute, tripTitle } from "./trip-route";

type Step = "acknowledge" | "pickup" | "drop" | "complete" | "done" | "closed";

/**
 * GEOFENCE_REQUIRED means the server wanted coordinates and got none, which
 * only happens when the fix failed — so the operator needs to hear what went
 * wrong with their location, not a constant they cannot act on.
 */
function explainLegFailure(
  err: unknown,
  geoReason: string | null,
  fallback: string,
): string {
  const message = err instanceof Error ? err.message : fallback;
  return geoReason && message.includes("GEOFENCE_REQUIRED")
    ? `${geoReason} This trip is fenced, so it can't be recorded without one.`
    : message;
}

// Phase 4.5 — Trip action workflow. Drives the operator through the
// FSM transitions one at a time:
//   New → Acknowledge → Pickup (GPS+POD) → Drop (GPS+POD) → Complete
//
// Each action gates the next: the button stays disabled until the
// previous step's timestamp is set on the server-returned trip data
// (which the poll picks up after the queue drains).
//
// Why the polled-trip pattern instead of optimistic local state:
//   - Source of truth is the server. If the operator force-closes the
//     PWA after acknowledging, the next launch reads from the server
//     and shows the correct next step.
//   - Offline queue dedupe still prevents duplicate acks via the
//     dedupeKey path in the queue helper.
export function TripDetail({ tripId }: { tripId: string }) {
  const router = useRouter();
  const [trip, setTrip] = useState<AssignedTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [pickupPodKey, setPickupPodKey] = useState<string | null>(null);
  const [dropPodKey, setDropPodKey] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState<Step | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionNote, setActionNote] = useState<string | null>(null);
  // Why the last load failed. Only shown while no trip has loaded yet: a
  // failed first load is not "not found" (offline, 429, 5xx).
  const [loadError, setLoadError] = useState<string | null>(null);
  const [retrying, setRetrying] = useState(false);

  const refresh = async () => {
    try {
      const all = await getAssignedTrips();
      const found = all.find((t) => t.tripId === tripId) ?? null;
      setTrip(found);
      setLoadError(null);
    } catch (err) {
      // Network blip — keep stale state visible, but let the schedule know so
      // it slows down rather than asking again every 8 seconds.
      setLoadError(err instanceof Error ? err.message : "Load failed.");
      throw err;
    } finally {
      setLoading(false);
    }
  };

  // The re-read after an action. Its failure is not the action's: offline,
  // the action was just queued ("Saved offline") and this fetch is bound to
  // fail — reporting that as the action's error hid the note behind a red
  // banner. The poll picks the trip up once the connection is back.
  const refreshAfterAction = () => refresh().catch(() => {});

  const retryLoad = async () => {
    setRetrying(true);
    await refresh().catch(() => {});
    setRetrying(false);
  };

  usePollSchedule(
    useCallback(() => refresh(), [tripId]),
    { intervalMs: 8_000 },
  );

  if (loading) return <div className="p-6 text-sm text-zinc-500">Loading…</div>;
  if (!trip && loadError) {
    return (
      <div
        role="alert"
        className="m-4 flex flex-col items-center gap-3 rounded-xl border border-rose-900/60 bg-rose-950/40 p-6 text-center text-sm text-rose-200"
      >
        <div>
          <div className="font-medium">โหลด trip ไม่สำเร็จ</div>
          <div className="mt-1 text-xs opacity-80">
            ตรวจ Wi-Fi แล้วลองใหม่ — งานนี้ยังไม่ได้ถูกยกเลิก
          </div>
        </div>
        <button
          type="button"
          onClick={() => void retryLoad()}
          disabled={retrying}
          className="min-h-11 rounded-lg bg-zinc-100 px-5 text-sm font-medium text-zinc-900 disabled:opacity-50"
        >
          {retrying ? "กำลังโหลด…" : "ลองใหม่"}
        </button>
      </div>
    );
  }
  if (!trip) {
    return (
      <div className="m-4 rounded-xl border border-zinc-800 bg-zinc-900/50 p-6 text-center text-sm text-zinc-400">
        Trip not found or no longer assigned to you.
      </div>
    );
  }

  // Ended on the dispatch side (cancelled, failed) — nothing left for the
  // operator, whatever their own timestamps say.
  const ended = tripEndedLabel(trip);
  const step: Step = ended
    ? "closed"
    : !trip.acknowledgedAt
      ? "acknowledge"
      : !trip.pickedUpAt
        ? "pickup"
        : !trip.droppedAt
          ? "drop"
          : !trip.completedAt
            ? "complete"
            : "done";

  const onAcknowledge = async () => {
    setActionBusy("acknowledge");
    setActionError(null);
    setActionNote(null);
    try {
      const result = await acknowledgeTrip(tripId);
      setActionNote(result.delivered ? "Acknowledged." : "Saved offline — will sync.");
      await refreshAfterAction();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Acknowledge failed.");
    } finally {
      setActionBusy(null);
    }
  };

  // A failed fix no longer ends the leg here. It used to: a tablet on an HTTP
  // LAN address gets PERMISSION_DENIED from Chromium whatever the permission
  // says, which made pickup unreachable on the very devices this app is for —
  // even though the geofence is off and the backend never wanted coordinates.
  const onPickup = async () => {
    setActionBusy("pickup");
    setActionError(null);
    setActionNote(null);
    const { fix, reason } = await tryGetCurrentPosition();
    try {
      const result = await recordPickup(tripId, {
        lat: fix?.lat ?? null,
        lng: fix?.lng ?? null,
        podKey: pickupPodKey,
      });
      setActionNote(result.delivered ? "Pickup recorded." : "Saved offline — will sync.");
      await refreshAfterAction();
    } catch (err) {
      setActionError(explainLegFailure(err, reason, "Pickup failed."));
    } finally {
      setActionBusy(null);
    }
  };

  const onDrop = async () => {
    setActionBusy("drop");
    setActionError(null);
    setActionNote(null);
    const { fix, reason } = await tryGetCurrentPosition();
    try {
      const result = await recordDrop(tripId, {
        lat: fix?.lat ?? null,
        lng: fix?.lng ?? null,
        podKey: dropPodKey,
      });
      setActionNote(result.delivered ? "Drop recorded." : "Saved offline — will sync.");
      await refreshAfterAction();
    } catch (err) {
      setActionError(explainLegFailure(err, reason, "Drop failed."));
    } finally {
      setActionBusy(null);
    }
  };

  const onComplete = async () => {
    setActionBusy("complete");
    setActionError(null);
    setActionNote(null);
    try {
      const result = await completeTrip(tripId);
      if (result.delivered) {
        // Nothing left to do on this trip — back to the list for the next.
        router.push("/m/trips");
        return;
      }
      setActionNote("Saved offline — will sync.");
      await refreshAfterAction();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Complete failed.");
    } finally {
      setActionBusy(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      <header className="rounded-2xl border border-zinc-800 bg-zinc-900/60 p-4">
        <div className="flex items-center justify-between">
          <div>
            <div className="text-xs uppercase tracking-wide text-zinc-500">Order</div>
            <div className="mt-0.5 font-mono text-sm text-zinc-100">
              {tripTitle(trip)}
            </div>
          </div>
          <StepBadge step={step} label={ended} />
        </div>
        <TripRoute trip={trip} />
        {/* Kept small for support: the id dispatch and admin search by. */}
        {trip.orderRef && (
          <div className="mt-2 font-mono text-[11px] text-zinc-600">
            Trip {trip.tripId.slice(0, 8)}
          </div>
        )}
        <ProgressTimeline trip={trip} />
      </header>

      {actionError && <ErrorBanner message={actionError} />}
      {actionNote && !actionError && (
        <div className="rounded-xl border border-emerald-900/60 bg-emerald-950/40 p-3 text-sm text-emerald-200">
          {actionNote}
        </div>
      )}

      <ActionPanel
        step={step}
        trip={trip}
        actionBusy={actionBusy}
        pickupPodKey={pickupPodKey}
        dropPodKey={dropPodKey}
        onPickupPodCaptured={setPickupPodKey}
        onDropPodCaptured={setDropPodKey}
        onAcknowledge={onAcknowledge}
        onPickup={onPickup}
        onDrop={onDrop}
        onComplete={onComplete}
      />
    </div>
  );
}

function StepBadge({ step, label }: { step: Step; label: string | null }) {
  const map: Record<Step, { text: string; cls: string }> = {
    acknowledge: { text: "New", cls: "bg-zinc-100/15 text-zinc-100" },
    pickup: { text: "En route", cls: "bg-amber-500/15 text-amber-300" },
    drop: { text: "In transit", cls: "bg-cyan-500/15 text-cyan-300" },
    complete: { text: "Ready to close", cls: "bg-blue-500/15 text-blue-300" },
    done: { text: "Done", cls: "bg-emerald-500/15 text-emerald-300" },
    closed: { text: "Closed", cls: "bg-red-500/15 text-red-300" },
  };
  const { text, cls } = map[step];
  return (
    <span className={`rounded-full px-3 py-1 text-xs font-medium ${cls}`}>{label ?? text}</span>
  );
}

function ProgressTimeline({ trip }: { trip: AssignedTrip }) {
  const rows: { label: string; iso: string | null; done: boolean }[] = [
    { label: "Assigned", iso: trip.assignedAt, done: true },
    { label: "Acknowledged", iso: trip.acknowledgedAt, done: !!trip.acknowledgedAt },
    { label: "Picked up", iso: trip.pickedUpAt, done: !!trip.pickedUpAt },
    { label: "Dropped", iso: trip.droppedAt, done: !!trip.droppedAt },
    { label: "Completed", iso: trip.completedAt, done: !!trip.completedAt },
  ];
  return (
    <ol className="mt-4 flex flex-col gap-2 border-t border-zinc-800 pt-3 text-sm">
      {rows.map((r) => (
        <li key={r.label} className="flex items-center justify-between">
          <span className={r.done ? "text-zinc-100" : "text-zinc-500"}>
            <span
              className={`mr-2 inline-block size-1.5 rounded-full align-middle ${
                r.done ? "bg-emerald-400" : "bg-zinc-600"
              }`}
            />
            {r.label}
          </span>
          <span className="font-mono text-xs text-zinc-500">
            {r.iso ? new Date(r.iso).toLocaleTimeString() : "—"}
          </span>
        </li>
      ))}
    </ol>
  );
}

function ActionPanel(props: {
  step: Step;
  trip: AssignedTrip;
  actionBusy: Step | null;
  pickupPodKey: string | null;
  dropPodKey: string | null;
  onPickupPodCaptured: (k: string) => void;
  onDropPodCaptured: (k: string) => void;
  onAcknowledge: () => void;
  onPickup: () => void;
  onDrop: () => void;
  onComplete: () => void;
}) {
  const {
    step,
    trip,
    actionBusy,
    pickupPodKey,
    dropPodKey,
    onPickupPodCaptured,
    onDropPodCaptured,
    onAcknowledge,
    onPickup,
    onDrop,
    onComplete,
  } = props;
  if (step === "acknowledge") {
    return (
      <button
        onClick={onAcknowledge}
        disabled={actionBusy !== null}
        className="h-14 rounded-2xl bg-zinc-100 text-base font-semibold text-zinc-950 disabled:opacity-50"
      >
        {actionBusy === "acknowledge" ? "Acknowledging…" : "Acknowledge trip"}
      </button>
    );
  }
  if (step === "pickup") {
    return (
      <div className="flex flex-col gap-3">
        <PodCapture
          tripId={trip.tripId}
          kind="pickup"
          podKey={pickupPodKey}
          onCaptured={onPickupPodCaptured}
        />
        <button
          onClick={onPickup}
          disabled={actionBusy !== null}
          className="h-14 rounded-2xl bg-amber-400 text-base font-semibold text-zinc-950 disabled:opacity-50"
        >
          {actionBusy === "pickup" ? "Recording…" : "Record pickup at my location"}
        </button>
      </div>
    );
  }
  if (step === "drop") {
    return (
      <div className="flex flex-col gap-3">
        <PodCapture
          tripId={trip.tripId}
          kind="drop"
          podKey={dropPodKey}
          onCaptured={onDropPodCaptured}
        />
        <button
          onClick={onDrop}
          disabled={actionBusy !== null}
          className="h-14 rounded-2xl bg-cyan-400 text-base font-semibold text-zinc-950 disabled:opacity-50"
        >
          {actionBusy === "drop" ? "Recording…" : "Record drop at my location"}
        </button>
      </div>
    );
  }
  if (step === "complete") {
    return (
      <button
        onClick={onComplete}
        disabled={actionBusy !== null}
        className="h-14 rounded-2xl bg-emerald-400 text-base font-semibold text-zinc-950 disabled:opacity-50"
      >
        {actionBusy === "complete" ? "Completing…" : "Complete trip"}
      </button>
    );
  }
  if (step === "closed") {
    return (
      <div className="rounded-2xl border border-red-900/60 bg-red-950/40 p-4 text-sm text-red-200">
        {tripEndedLabel(trip) === "Cancelled"
          ? "This trip was cancelled by dispatch."
          : "This trip was ended by dispatch."}{" "}
        Nothing more to do here — check with your supervisor about any cargo
        you already picked up.
      </div>
    );
  }
  return null;
}

function ErrorBanner({ message }: { message: string }) {
  // Geofence rejects come through prefixed GEOFENCE_REJECTED. The in-app
  // "request override" form is hidden until geofencing is switched on
  // (Wms:Geofence:Enabled is false, no override has ever been filed): it
  // asked the operator to type a warehouse id the app never shows, and sent
  // it under a field name the API didn't read (since corrected in
  // SubmitOverrideBody) — it could not succeed. Before enabling the
  // geofence, have the API resolve the leg's location from the trip and
  // bring the form back (review M43).
  const isGeofence = message.startsWith("GEOFENCE_REJECTED");
  return (
    <div className="rounded-xl border border-red-900/60 bg-red-950/40 p-3 text-sm text-red-200">
      <div className="font-medium">{message}</div>
      {isGeofence && (
        <div className="mt-2 border-t border-red-900/60 pt-2 text-xs">
          อยู่นอกพื้นที่ของจุดนี้ — แจ้งหัวหน้างานเพื่อขอ override
        </div>
      )}
    </div>
  );
}
