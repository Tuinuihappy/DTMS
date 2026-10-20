"use client";

import { usePathname } from "next/navigation";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  type ReactNode,
} from "react";
import { getPoolTrips, type PoolTrip } from "@/lib/api/operator-pool";
import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";
import { POOL_FALLBACK_POLL_MS, VISIBILITY_REFETCH_MIN_MS } from "@/lib/operator-pwa/alert-config";
import { initialPoolWatch, observePool } from "@/lib/operator-pwa/pool-alert-logic";
import { useOperatorAlerts } from "./operator-alerts";

// WMS PR-4b (PR-D) — Operator pool state, owned once for every /m page.
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
//   - FAILED keeps the last list: a failed fetch (offline, 5xx) must not
//     make the pool look empty.
//   - Ordering is stable by dispatchedAt ASC (FIFO); server rows are
//     already in that order, and ADDED items are re-sorted on insert.
//
// Why a provider in the /m layout and not a hook per page: there must be
// exactly ONE subscription to /hubs/operator-pool. The connection is shared
// per hub, and a subscriber's unmount invokes Unsubscribe, which takes the
// whole connection out of the server's pool group
// (OperatorPoolHub.Unsubscribe → RemoveFromGroupAsync). A second
// subscriber — say a page-level one beside this — would go deaf the moment
// the other unmounted. Read pool state through usePoolTrips() instead.

type PoolAction =
  | { type: "REFETCH"; trips: PoolTrip[] }
  | { type: "FAILED"; error: string }
  | { type: "ADDED"; trip: PoolTrip }
  | { type: "REMOVED"; tripId: string };

type State = {
  trips: PoolTrip[];
  loading: boolean;
  error: string | null;
  /** A REST load has succeeded at least once — the alert baseline. */
  loaded: boolean;
};

const initialState: State = { trips: [], loading: true, error: null, loaded: false };

function reducer(state: State, action: PoolAction): State {
  switch (action.type) {
    case "REFETCH":
      return { trips: sortFifo(action.trips), loading: false, error: null, loaded: true };
    case "FAILED":
      return { ...state, loading: false, error: action.error };
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

export type OperatorPoolValue = {
  trips: PoolTrip[];
  loading: boolean;
  error: string | null;
  connected: boolean;
  refetch: () => Promise<void>;
  /** Hide a card between tap and 204 without waiting for the broadcast
   *  round trip. If the POST 409s the caller re-invokes refetch(). */
  optimisticRemove: (tripId: string) => void;
};

export const OperatorPoolContext = createContext<OperatorPoolValue | null>(null);

/** Pool state, or null outside the /m layout (e.g. for the tab badge). */
export function useOptionalOperatorPool(): OperatorPoolValue | null {
  return useContext(OperatorPoolContext);
}

const POOL_ALERT_KEY = "pool-new";

export function OperatorPoolProvider({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  // The layout also wraps /m/login: no session there, so the hub token and
  // pool fetch would only 401.
  const enabled = !pathname?.startsWith("/m/login");
  const [state, dispatch] = useReducer(reducer, initialState);
  const alerts = useOperatorAlerts();
  const lastFetchAt = useRef(0);
  // Fetches overlap (page open, reconnect, fallback poll, screen-on), and
  // an older one can answer last. Only the newest may write: an older list
  // would bring back a trip that was claimed since, and an older failure
  // would raise an error the newer success already disproved.
  const fetchSeq = useRef(0);

  const refetch = useCallback(async () => {
    const seq = ++fetchSeq.current;
    lastFetchAt.current = Date.now();
    try {
      const trips = await getPoolTrips();
      if (seq !== fetchSeq.current) return;
      dispatch({ type: "REFETCH", trips });
    } catch (err) {
      if (seq !== fetchSeq.current) return;
      dispatch({ type: "FAILED", error: err instanceof Error ? err.message : "Pool fetch failed." });
      console.warn("[pool] REST refetch failed:", err);
    }
  }, []);

  // Initial fetch, and again whenever the operator comes back from the
  // login page.
  useEffect(() => {
    if (enabled) void refetch();
  }, [enabled, refetch]);

  // Events between the initial fetch and this handler attaching will be
  // missed, but the REST fetch above is fresh and only ~50-200ms of race
  // window exists — acceptable for a pool that turns over in seconds.
  const { connected } = useHubSubscription({
    hubPath: "/hubs/operator-pool",
    subscribeMethod: "Subscribe",
    unsubscribeMethod: "Unsubscribe",
    subscribeArgs: [],
    enabled,
    eventHandlers: {
      PoolTripAdded: (trip: unknown) => dispatch({ type: "ADDED", trip: trip as PoolTrip }),
      PoolTripClaimed: (claim: unknown) => {
        const c = claim as { tripId: string };
        dispatch({ type: "REMOVED", tripId: c.tripId });
      },
      PoolTripRemoved: (removal: unknown) => {
        const r = removal as { tripId: string };
        dispatch({ type: "REMOVED", tripId: r.tripId });
      },
      // Sent only to this operator (server group operator:{EmployeeCode}):
      // a trip they claimed was ended by dispatch. Rides this subscription
      // because there must be only one on /hubs/operator-pool (see above).
      AssignedTripEnded: (payload: unknown) => {
        const e = payload as { tripId: string; outcome?: string; reason?: string };
        alerts?.raise(
          {
            key: `trip-ended:${e.tripId}`,
            kind: "trip-ended",
            title: e.outcome === "cancelled" ? "Trip ของคุณถูกยกเลิก" : "Trip ของคุณถูกยุติโดย dispatch",
            detail: [`Trip ${e.tripId.slice(0, 8)}`, e.reason].filter(Boolean).join(" · "),
            href: `/m/trips/${e.tripId}`,
          },
          { sound: true },
        );
      },
    },
  });

  // On reconnect, re-fetch REST to reconcile any missed events during the
  // gap. `connected` false → true fires refetch; the first connect races
  // the initial fetch above — fine, the request is small.
  useEffect(() => {
    if (enabled && connected) void refetch();
  }, [enabled, connected, refetch]);

  // Fallback while the realtime feed is down (hub unreachable, wrong hub
  // URL baked into the build): poll, so new trips still alert — late, not
  // never. The list-diff below doesn't care where a change came from.
  useEffect(() => {
    if (!enabled || connected) return;
    const id = window.setInterval(() => void refetch(), POOL_FALLBACK_POLL_MS);
    return () => window.clearInterval(id);
  }, [enabled, connected, refetch]);

  // Screen back on / app back in front: the socket may be dead without
  // knowing it yet, so don't trust `connected` — reload, and anything that
  // landed meanwhile alerts through the diff.
  useEffect(() => {
    if (!enabled) return;
    const onVisible = () => {
      if (document.visibilityState !== "visible") return;
      if (Date.now() - lastFetchAt.current < VISIBILITY_REFETCH_MIN_MS) return;
      void refetch();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => document.removeEventListener("visibilitychange", onVisible);
  }, [enabled, refetch]);

  // ── New-trip alert ────────────────────────────────────────────────────
  // Every change to the list is diffed against what was already seen
  // (pool-alert-logic). New ids → banner (+ sound, coalesced). The banner
  // tracks which of its trips are still waiting and closes itself once
  // they've all been claimed or cancelled, or when the pool page opens.
  const watch = useRef(initialPoolWatch);
  const pending = useRef<string[]>([]);
  const raise = alerts?.raise;
  const dismissAlert = alerts?.dismiss;
  const sound = alerts?.sound;
  const onPoolPage = pathname === "/m/pool";

  useEffect(() => {
    if (!state.loaded || !raise || !dismissAlert) return;
    const ids = state.trips.map((t) => t.tripId);
    const obs = observePool(watch.current, ids, Date.now());
    watch.current = obs.watch;

    const inPool = new Set(ids);
    const waiting = onPoolPage ? [] : [...pending.current, ...obs.newIds].filter((id) => inPool.has(id));
    pending.current = waiting;

    if (obs.chime && onPoolPage && sound) sound("pool-new"); // cards are in view; noise only
    if (waiting.length === 0) {
      dismissAlert(POOL_ALERT_KEY);
      return;
    }
    const latest = state.trips.find((t) => t.tripId === waiting[waiting.length - 1]);
    raise(
      {
        key: POOL_ALERT_KEY,
        kind: "pool-new",
        title: waiting.length === 1 ? "งานใหม่ใน Pool" : `งานใหม่ใน Pool (${waiting.length})`,
        detail: latest
          ? `${latest.orderRef || "—"} · ${latest.pickupCode || "?"} → ${latest.dropCode || "?"}`
          : undefined,
        href: "/m/pool",
        onDismiss: () => {
          pending.current = [];
        },
      },
      { sound: obs.chime },
    );
  }, [state.trips, state.loaded, onPoolPage, raise, dismissAlert, sound]);

  const optimisticRemove = useCallback(
    (tripId: string) => dispatch({ type: "REMOVED", tripId }),
    [],
  );

  const value = useMemo<OperatorPoolValue>(
    () => ({
      trips: state.trips,
      loading: state.loading,
      error: state.error,
      connected,
      refetch,
      optimisticRemove,
    }),
    [state, connected, refetch, optimisticRemove],
  );

  return <OperatorPoolContext.Provider value={value}>{children}</OperatorPoolContext.Provider>;
}
