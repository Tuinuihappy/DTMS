"use client";

import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";

const PATH = "/hubs/orders";

// Phase P0 Day 5 — typed wrapper for the Order hub. Components import
// these wrappers instead of useHubSubscription directly so the call
// sites stay readable and refactor-safe (rename a hub method? touch one
// file instead of N).

export type OrderHubEvents = {
  /** New row appended to the order's status_history projection. */
  TimelineUpdated?: (entry: unknown) => void;
  /** Coarse "status changed" event for badges + list view rows. */
  StatusChanged?: (change: unknown) => void;
  /** Phase P2 — unified activity timeline entry. */
  ActivityUpdated?: (entry: unknown) => void;
};

export function useOrderHubSubscription(
  orderId: string | null,
  events: OrderHubEvents,
) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "Subscribe",
    unsubscribeMethod: "Unsubscribe",
    subscribeArgs: [orderId],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
    enabled: !!orderId,
  });
}

export const ORDER_HUB_PATH = PATH;
