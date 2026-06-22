"use client";

import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";

const PATH = "/hubs/trips";

export type TripHubEvents = {
  TimelineUpdated?: (entry: unknown) => void;
  StatusChanged?: (change: unknown) => void;
  MissionUpdated?: (missionEvent: unknown) => void;
};

export function useTripSubscription(tripId: string | null, events: TripHubEvents) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "Subscribe",
    unsubscribeMethod: "Unsubscribe",
    subscribeArgs: [tripId],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
    enabled: !!tripId,
  });
}

// Backend Phase 2 — cross-trip list subscription. Server pushes a tiny
// hint ({ tripId, toStatus }) whenever TripStatusHistoryProjector
// appends a row; the list page treats this as a debounce-refetch
// trigger (mirrors useOrderListSubscription, see order-hub.ts).
export type TripListHintPayload = {
  tripId: string;
  toStatus: string;
};

export type TripListHubEvents = {
  ListItemUpdated?: (hint: TripListHintPayload) => void;
};

export function useTripListSubscription(
  events: TripListHubEvents,
  enabled = true,
) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "SubscribeList",
    unsubscribeMethod: "UnsubscribeList",
    subscribeArgs: [],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
    enabled,
  });
}

export const TRIP_HUB_PATH = PATH;
