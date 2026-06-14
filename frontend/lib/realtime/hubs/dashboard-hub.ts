"use client";

import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";

const PATH = "/hubs/dashboard";

export type DashboardHubEvents = {
  /** 250 ms batched counter deltas, one push per board per tick. */
  CountersUpdated?: (deltas: unknown[]) => void;
  /** Full KPI snapshot — used on initial load / reconnect. */
  KpiSnapshotUpdated?: (snapshot: unknown) => void;
};

/**
 * @param boardKey - "orders" | "fleet" | "funnel" | "sla" — chooses which
 *                   board's projection updates this connection receives.
 */
export function useDashboardSubscription(
  boardKey: string | null,
  events: DashboardHubEvents,
) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "Subscribe",
    unsubscribeMethod: "Unsubscribe",
    subscribeArgs: [boardKey],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
    enabled: !!boardKey,
  });
}

export const DASHBOARD_HUB_PATH = PATH;
