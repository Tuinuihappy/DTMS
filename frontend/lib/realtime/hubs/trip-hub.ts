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

export const TRIP_HUB_PATH = PATH;
