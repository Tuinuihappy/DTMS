"use client";

import { useCallback, useState } from "react";
import {
  listActiveManualTrips,
  listOperators,
  listPendingOverrides,
  type ManualTripBoardRow,
  type OperatorBoardRow,
  type OverrideQueueRow,
} from "@/lib/api/admin-manual";
import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";
import { usePollSchedule } from "@/lib/hooks/use-poll-schedule";
import { OperatorBoardSection } from "./operator-board-section";
import { OverrideQueueSection } from "./override-queue-section";

// Phase 4.6 — Dispatcher console for Manual mode.
//
// Three-column layout (stacks on mobile):
//   • Operators — who's on shift, who's busy, who's available
//   • Active trips — what's currently being carried + by whom
//   • Override queue — pending geofence-override requests awaiting decision
//
// Realtime: subscribes to /hubs/manual-board for OverrideDecided +
// TripReassigned hints. Hints trigger a refetch of the relevant section
// — payloads are intentionally lightweight ({id, status}) so cross-page
// concerns can mutate freely without lockstep frontend deploys.
//
// Fallback polling: 12s for all three feeds — covers cases where the
// hub connection is severed and reconnect hasn't kicked in yet.
export function ManualOperatorBoard() {
  const [operators, setOperators] = useState<OperatorBoardRow[]>([]);
  const [trips, setTrips] = useState<ManualTripBoardRow[]>([]);
  const [overrides, setOverrides] = useState<OverrideQueueRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  // Three requests per refresh, so this board is the heaviest poller per tick.
  // Failures are rethrown for the schedule to back off on.
  const refreshAll = useCallback(async () => {
    try {
      const [ops, tr, ov] = await Promise.all([
        listOperators(),
        listActiveManualTrips(),
        listPendingOverrides(),
      ]);
      setOperators(ops);
      setTrips(tr);
      setOverrides(ov);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not load Manual board.");
      throw err;
    }
  }, []);

  // Fallback polling, now paused while the tab is hidden and slowed when the
  // API refuses — three endpoints every 12 seconds was the largest cost of
  // leaving this board open in a background tab.
  const { trigger } = usePollSchedule(refreshAll, { intervalMs: 12_000 });

  useHubSubscription({
    hubPath: "/hubs/manual-board",
    subscribeMethod: "Subscribe",
    unsubscribeMethod: "Unsubscribe",
    subscribeArgs: [],
    eventHandlers: {
      // Through the schedule: each hint costs three requests, and an
      // undebounced burst used to fire all of them per event.
      OverrideDecided: () => trigger(),
      TripReassigned: () => trigger(),
    },
  });

  return (
    <div className="flex flex-col gap-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Manual operators</h1>
          <p className="text-sm text-[var(--color-ink-500)]">
            Operator board, active trips, and override approvals — Manual transport mode.
          </p>
        </div>
        <button
          type="button"
          onClick={() => trigger()}
          className="rounded-lg border border-[var(--color-ink-200)] bg-[var(--color-surface)] px-3 py-1.5 text-xs text-[var(--color-ink-700)] hover:bg-[var(--color-surface-soft)]"
        >
          Refresh
        </button>
      </header>

      {error && (
        <div className="rounded-xl border border-[var(--color-coral)]/30 bg-[var(--color-coral-soft)] p-4 text-sm text-[var(--color-coral)]">
          {error}
        </div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <OperatorBoardSection
          operators={operators}
          trips={trips}
          onChange={() => trigger()}
        />
        <OverrideQueueSection
          overrides={overrides}
          operators={operators}
          onChange={() => trigger()}
        />
      </div>
    </div>
  );
}
