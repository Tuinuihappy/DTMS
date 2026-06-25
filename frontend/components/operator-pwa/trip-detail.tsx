"use client";

import { useEffect, useState } from "react";
import {
  acknowledgeTrip,
  completeTrip,
  getAssignedTrips,
  recordDrop,
  recordPickup,
  submitGeofenceOverride,
  type AssignedTrip,
} from "@/lib/api/operator";
import { getCurrentPosition } from "@/lib/operator-pwa/geolocation";
import { PodCapture } from "./pod-capture";

type Step = "acknowledge" | "pickup" | "drop" | "complete" | "done";

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
  const [trip, setTrip] = useState<AssignedTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [pickupPodKey, setPickupPodKey] = useState<string | null>(null);
  const [dropPodKey, setDropPodKey] = useState<string | null>(null);
  const [actionBusy, setActionBusy] = useState<Step | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionNote, setActionNote] = useState<string | null>(null);

  const refresh = async () => {
    try {
      const all = await getAssignedTrips();
      const found = all.find((t) => t.tripId === tripId) ?? null;
      setTrip(found);
    } catch {
      // Network blip — keep stale state visible.
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    refresh();
    const tick = window.setInterval(refresh, 8_000);
    return () => window.clearInterval(tick);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tripId]);

  if (loading) return <div className="p-6 text-sm text-zinc-500">Loading…</div>;
  if (!trip) {
    return (
      <div className="m-4 rounded-xl border border-zinc-800 bg-zinc-900/50 p-6 text-center text-sm text-zinc-400">
        Trip not found or no longer assigned to you.
      </div>
    );
  }

  const step: Step = !trip.acknowledgedAt
    ? "acknowledge"
    : !trip.pickedUpAt
      ? "pickup"
      : !trip.droppedAt
        ? "drop"
        : "complete";

  const onAcknowledge = async () => {
    setActionBusy("acknowledge");
    setActionError(null);
    setActionNote(null);
    try {
      const result = await acknowledgeTrip(tripId);
      setActionNote(result.delivered ? "Acknowledged." : "Saved offline — will sync.");
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Acknowledge failed.");
    } finally {
      setActionBusy(null);
    }
  };

  const onPickup = async () => {
    setActionBusy("pickup");
    setActionError(null);
    setActionNote(null);
    try {
      const fix = await getCurrentPosition();
      const result = await recordPickup(tripId, {
        lat: fix.lat,
        lng: fix.lng,
        podKey: pickupPodKey,
      });
      setActionNote(result.delivered ? "Pickup recorded." : "Saved offline — will sync.");
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Pickup failed.");
    } finally {
      setActionBusy(null);
    }
  };

  const onDrop = async () => {
    setActionBusy("drop");
    setActionError(null);
    setActionNote(null);
    try {
      const fix = await getCurrentPosition();
      const result = await recordDrop(tripId, {
        lat: fix.lat,
        lng: fix.lng,
        podKey: dropPodKey,
      });
      setActionNote(result.delivered ? "Drop recorded." : "Saved offline — will sync.");
      await refresh();
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "Drop failed.");
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
      setActionNote(result.delivered ? "Trip completed." : "Saved offline — will sync.");
      await refresh();
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
            <div className="text-xs uppercase tracking-wide text-zinc-500">Trip</div>
            <div className="mt-0.5 font-mono text-sm text-zinc-200">
              {trip.tripId.slice(0, 8)}
            </div>
          </div>
          <StepBadge step={step} />
        </div>
        <ProgressTimeline trip={trip} />
      </header>

      {actionError && (
        <ErrorBanner
          message={actionError}
          tripId={trip.tripId}
          onOverrideSubmitted={(note) => setActionNote(note)}
        />
      )}
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

function StepBadge({ step }: { step: Step }) {
  const map: Record<Step, { text: string; cls: string }> = {
    acknowledge: { text: "New", cls: "bg-zinc-100/15 text-zinc-100" },
    pickup: { text: "En route", cls: "bg-amber-500/15 text-amber-300" },
    drop: { text: "In transit", cls: "bg-cyan-500/15 text-cyan-300" },
    complete: { text: "Ready to close", cls: "bg-blue-500/15 text-blue-300" },
    done: { text: "Done", cls: "bg-emerald-500/15 text-emerald-300" },
  };
  const { text, cls } = map[step];
  return <span className={`rounded-full px-3 py-1 text-xs font-medium ${cls}`}>{text}</span>;
}

function ProgressTimeline({ trip }: { trip: AssignedTrip }) {
  const rows: { label: string; iso: string | null; done: boolean }[] = [
    { label: "Assigned", iso: trip.assignedAt, done: true },
    { label: "Acknowledged", iso: trip.acknowledgedAt, done: !!trip.acknowledgedAt },
    { label: "Picked up", iso: trip.pickedUpAt, done: !!trip.pickedUpAt },
    { label: "Dropped", iso: trip.droppedAt, done: !!trip.droppedAt },
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
  return null;
}

function ErrorBanner({
  message,
  tripId,
  onOverrideSubmitted,
}: {
  message: string;
  tripId: string;
  onOverrideSubmitted: (note: string) => void;
}) {
  // Geofence-reject errors come through with the prefix GEOFENCE_REJECTED.
  // Surface a follow-up "request override" affordance so the operator
  // can recover without restarting the workflow.
  const isGeofence = message.startsWith("GEOFENCE_REJECTED");
  const [warehouseId, setWarehouseId] = useState("");
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const submit = async () => {
    setSubmitting(true);
    try {
      const fix = await getCurrentPosition();
      await submitGeofenceOverride({
        tripId,
        expectedWarehouseId: warehouseId,
        lat: fix.lat,
        lng: fix.lng,
        reason,
        photoUrl: null,
      });
      onOverrideSubmitted("Override requested — supervisor will review.");
    } catch (err) {
      onOverrideSubmitted(err instanceof Error ? err.message : "Override request failed.");
    } finally {
      setSubmitting(false);
    }
  };
  return (
    <div className="rounded-xl border border-red-900/60 bg-red-950/40 p-3 text-sm text-red-200">
      <div className="font-medium">{message}</div>
      {isGeofence && (
        <div className="mt-3 flex flex-col gap-2 border-t border-red-900/60 pt-3 text-xs">
          <label className="flex flex-col gap-1">
            Warehouse ID (copy from the trip detail)
            <input
              value={warehouseId}
              onChange={(e) => setWarehouseId(e.target.value)}
              className="h-10 rounded-md border border-red-900/60 bg-zinc-950 px-2 font-mono text-red-100 outline-none"
            />
          </label>
          <label className="flex flex-col gap-1">
            Reason
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              rows={2}
              className="rounded-md border border-red-900/60 bg-zinc-950 px-2 py-1.5 text-red-100 outline-none"
            />
          </label>
          <button
            onClick={submit}
            disabled={submitting || !warehouseId || !reason}
            className="h-10 rounded-md bg-red-200 text-xs font-medium text-red-950 disabled:opacity-50"
          >
            {submitting ? "Requesting…" : "Request override"}
          </button>
        </div>
      )}
    </div>
  );
}
