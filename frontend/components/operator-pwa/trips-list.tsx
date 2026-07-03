"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { type AssignedTrip, getAssignedTrips } from "@/lib/api/operator";

type LoadState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "loaded"; trips: AssignedTrip[] }
  | { kind: "error"; message: string };

// Phase 4.5 — Trips list. Polls every 10 seconds to pick up assignments
// arriving from the dispatcher console (push gives instant feedback when
// the device is online; the poll is the fallback + the catchup mechanism
// when push has been declined/blocked).
export function TripsList() {
  const [state, setState] = useState<LoadState>({ kind: "idle" });

  useEffect(() => {
    let cancelled = false;
    const fetchOnce = async () => {
      try {
        const trips = await getAssignedTrips();
        if (!cancelled) setState({ kind: "loaded", trips });
      } catch (err) {
        if (!cancelled)
          setState({
            kind: "error",
            message: err instanceof Error ? err.message : "Could not load trips.",
          });
      }
    };
    setState({ kind: "loading" });
    fetchOnce();
    const tick = window.setInterval(fetchOnce, 10_000);
    return () => {
      cancelled = true;
      window.clearInterval(tick);
    };
  }, []);

  if (state.kind === "loading" || state.kind === "idle") {
    return <div className="p-6 text-sm text-zinc-500">Loading…</div>;
  }
  if (state.kind === "error") {
    return (
      <div className="m-4 rounded-xl border border-red-900/60 bg-red-950/40 p-4 text-sm text-red-200">
        {state.message}
      </div>
    );
  }
  if (state.trips.length === 0) {
    return (
      <div className="m-4 rounded-xl border border-zinc-800 bg-zinc-900/50 p-6 text-center text-sm text-zinc-400">
        No trips assigned yet. Check back in a moment.
      </div>
    );
  }

  // Sort: in-progress (acknowledged + not yet done) first, then new
  // assignments waiting for ack, then finished (dropped). Within each
  // bucket newest first by AssignedAt.
  const sorted = [...state.trips].sort((a, b) => {
    const bucket = (t: AssignedTrip) =>
      t.droppedAt ? 2 : t.acknowledgedAt ? 0 : 1;
    const diff = bucket(a) - bucket(b);
    if (diff !== 0) return diff;
    return new Date(b.assignedAt).getTime() - new Date(a.assignedAt).getTime();
  });

  return (
    <ul className="flex flex-col gap-3 px-4 py-4">
      {sorted.map((trip) => (
        <li key={trip.tripId}>
          <Link
            href={`/m/trips/${trip.tripId}`}
            className="block rounded-2xl border border-zinc-800 bg-zinc-900/60 p-4 active:scale-[0.99] transition-transform"
          >
            <div className="flex items-start justify-between gap-3">
              <div>
                <div className="text-xs uppercase tracking-wide text-zinc-500">
                  Trip
                </div>
                <div className="mt-1 font-mono text-sm text-zinc-200">
                  {trip.tripId.slice(0, 8)}
                </div>
              </div>
              <TripStatusBadge trip={trip} />
            </div>
            <DeadlineFooter trip={trip} />
          </Link>
        </li>
      ))}
    </ul>
  );
}

function TripStatusBadge({ trip }: { trip: AssignedTrip }) {
  if (trip.droppedAt)
    return (
      <span className="rounded-full bg-emerald-500/15 px-2.5 py-1 text-xs font-medium text-emerald-300">
        Delivered
      </span>
    );
  if (trip.pickedUpAt)
    return (
      <span className="rounded-full bg-cyan-500/15 px-2.5 py-1 text-xs font-medium text-cyan-300">
        In transit
      </span>
    );
  if (trip.acknowledgedAt)
    return (
      <span className="rounded-full bg-amber-500/15 px-2.5 py-1 text-xs font-medium text-amber-300">
        En route
      </span>
    );
  return (
    <span className="rounded-full bg-zinc-100/15 px-2.5 py-1 text-xs font-medium text-zinc-100">
      New
    </span>
  );
}

function DeadlineFooter({ trip }: { trip: AssignedTrip }) {
  // Show the nearest unfulfilled deadline. Skip past deadlines on a
  // step the operator has already completed. Pool claim + ack happen
  // atomically so there is no separate AckDeadline anymore.
  const next = !trip.pickedUpAt
    ? trip.pickupDeadline && { label: "Pickup by", iso: trip.pickupDeadline }
    : !trip.droppedAt
      ? trip.dropDeadline && { label: "Drop by", iso: trip.dropDeadline }
      : null;
  if (!next) return null;
  return (
    <div className="mt-3 flex items-center justify-between border-t border-zinc-800 pt-3 text-xs text-zinc-400">
      <span>{next.label}</span>
      <RelativeTime iso={next.iso} />
    </div>
  );
}

function RelativeTime({ iso }: { iso: string }) {
  const now = Date.now();
  const ms = new Date(iso).getTime() - now;
  const minutes = Math.round(ms / 60_000);
  if (Number.isNaN(minutes)) return <span>—</span>;
  if (minutes < 0)
    return <span className="text-red-300">{Math.abs(minutes)} min late</span>;
  if (minutes < 60) return <span>in {minutes} min</span>;
  const hours = Math.round(minutes / 60);
  return <span>in {hours} h</span>;
}
