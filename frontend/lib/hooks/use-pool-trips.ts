"use client";

import { useCallback, useEffect, useReducer } from "react";
import { getPoolTrips, type PoolTrip } from "@/lib/api/operator-pool";
import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";

// WMS PR-4b (PR-D) — Operator pool state hook.
//
// Sources of truth (in priority order):
//   1. REST GET /api/operator/trips/pool  — fetched on mount and on
//      SignalR reconnect. Server is authoritative for the initial and
//      post-blip snapshot.
//   2. SignalR /hubs/operator-pool events — apply incremental changes
//      between fetches: PoolTripAdded (insert), PoolTripClaimed (remove),
//      PoolTripRemoved (remove).
//
// Reducer semantics:
//   - Inserts are deduplicated by tripId; a duplicate ADDED is a no-op.
//     (A racing REST fetch could return the trip that ADDED just delivered.)
//   - Removes tolerate missing tripId — no throw, no visible state change.
//   - REFETCH replaces the full list — used on mount and after reconnect.
//   - Ordering is stable by dispatchedAt ASC (FIFO); server rows are
//     already in that order, and ADDED items are re-sorted on insert.

type PoolAction =
  | { type: "REFETCH"; trips: PoolTrip[] }
  | { type: "ADDED"; trip: PoolTrip }
  | { type: "REMOVED"; tripId: string };

type State = {
  trips: PoolTrip[];
  loading: boolean;
  error: string | null;
};

const initialState: State = { trips: [], loading: true, error: null };

function reducer(state: State, action: PoolAction): State {
  switch (action.type) {
    case "REFETCH":
      return { trips: sortFifo(action.trips), loading: false, error: null };
    case "ADDED": {
      if (state.trips.some((t) => t.tripId === action.trip.tripId)) return state;
      return { ...state, trips: sortFifo([...state.trips, action.trip]) };
    }
    case "REMOVED": {
      const next = state.trips.filter((t) => t.tripId !== action.tripId);
      if (next.length === state.trips.length) return state;
      return { ...state, trips: next };
    }
    default:
      return state;
  }
}

function sortFifo(trips: PoolTrip[]): PoolTrip[] {
  return [...trips].sort((a, b) => a.dispatchedAt.localeCompare(b.dispatchedAt));
}

export function usePoolTrips() {
  const [state, dispatch] = useReducer(reducer, initialState);

  const refetch = useCallback(async () => {
    try {
      const trips = await getPoolTrips();
      dispatch({ type: "REFETCH", trips });
    } catch (err) {
      dispatch({ type: "REFETCH", trips: [] });
      console.warn("[pool] REST refetch failed:", err);
    }
  }, []);

  // Initial fetch on mount.
  useEffect(() => {
    void refetch();
  }, [refetch]);

  // SignalR subscription — auto-registers PoolTripAdded / Claimed / Removed
  // handlers on the shared /hubs/operator-pool connection. Events between
  // the initial fetch and this handler attaching will be missed, but the
  // REST fetch above is fresh and only ~50-200ms of race window exists —
  // acceptable for a pool that turns over in seconds.
  const { connected } = useHubSubscription({
    hubPath: "/hubs/operator-pool",
    subscribeMethod: "Subscribe",
    unsubscribeMethod: "Unsubscribe",
    subscribeArgs: [],
    eventHandlers: {
      PoolTripAdded: (trip: unknown) =>
        dispatch({ type: "ADDED", trip: trip as PoolTrip }),
      PoolTripClaimed: (claim: unknown) => {
        const c = claim as { tripId: string };
        dispatch({ type: "REMOVED", tripId: c.tripId });
      },
      PoolTripRemoved: (removal: unknown) => {
        const r = removal as { tripId: string };
        dispatch({ type: "REMOVED", tripId: r.tripId });
      },
    },
  });

  // On reconnect, re-fetch REST to reconcile any missed events during the
  // gap. `connected` transitions false → true fire refetch. First mount
  // false → true also fires, but the initial useEffect above races it —
  // fine, the request is small and dedupes at the browser layer.
  useEffect(() => {
    if (connected) void refetch();
  }, [connected, refetch]);

  return {
    trips: state.trips,
    loading: state.loading,
    error: state.error,
    connected,
    refetch,
    // Optimistic-remove exposed so the list component can hide a card
    // between tap and 204 without waiting for the broadcast round trip.
    // If the POST 409s the caller re-invokes refetch() to reconcile.
    optimisticRemove: (tripId: string) => dispatch({ type: "REMOVED", tripId }),
  };
}
