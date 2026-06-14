"use client";

import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";

const PATH = "/hubs/fleet";

export type FleetHubEvents = {
  /** 1-second batched latest-position-per-robot for the subscribed floor. */
  RobotPositionsUpdated?: (positions: unknown[]) => void;
  /** Rare lifecycle change — fired individually, not batched. */
  RobotStateChanged?: (robotId: string, state: string) => void;
};

export function useFleetFloorSubscription(
  facilityId: string | null,
  events: FleetHubEvents,
) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "SubscribeFloor",
    unsubscribeMethod: "UnsubscribeFloor",
    subscribeArgs: [facilityId],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
    enabled: !!facilityId,
  });
}

export const FLEET_HUB_PATH = PATH;
