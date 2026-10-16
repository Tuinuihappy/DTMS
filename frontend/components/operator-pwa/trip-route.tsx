import type { AssignedTrip } from "@/lib/api/operator";

/**
 * Pickup → drop and the item line for a claimed trip, laid out like the
 * pool card (`PoolCard` in pool-trips-list.tsx) so the operator sees the
 * same thing before and after claiming.
 */
export function TripRoute({ trip }: { trip: AssignedTrip }) {
  const items = trip.itemCount ?? 0;
  const weight = trip.totalWeightKg ?? 0;
  return (
    <>
      <div className="mt-3 grid grid-cols-[1fr_auto_1fr] items-center gap-2 text-xs text-zinc-300">
        <div className="truncate rounded-md bg-zinc-950 px-2 py-1.5 font-mono text-[11px]">
          {trip.pickupCode || "—"}
        </div>
        <span className="text-zinc-600">→</span>
        <div className="truncate rounded-md bg-zinc-950 px-2 py-1.5 font-mono text-[11px]">
          {trip.dropCode || "—"}
        </div>
      </div>
      {items > 0 && (
        <div className="mt-2 text-xs text-zinc-500">
          {items} item{items === 1 ? "" : "s"}
          {weight > 0 ? ` · ${weight.toFixed(1)} kg` : ""}
        </div>
      )}
    </>
  );
}

/** The order ref, or the short trip id when the trip has no item rows. */
export function tripTitle(trip: AssignedTrip): string {
  return trip.orderRef || trip.tripId.slice(0, 8);
}
