"use client";

import { useHubSubscription } from "@/lib/hooks/use-hub-subscription";

const PATH = "/hubs/jobs";

export type JobHubEvents = {
  TimelineUpdated?: (entry: unknown) => void;
  JobUpdated?: (job: unknown) => void;
  JobAdded?: (job: unknown) => void;
  JobRemoved?: (jobId: string) => void;
};

/** Jobs queue page — receives the cross-order queue stream. */
export function useJobQueueSubscription(events: JobHubEvents) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "SubscribeQueue",
    unsubscribeMethod: "UnsubscribeQueue",
    subscribeArgs: [],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
  });
}

/** Job detail drawer — receives updates scoped to a single job. */
export function useJobSubscription(jobId: string | null, events: JobHubEvents) {
  return useHubSubscription({
    hubPath: PATH,
    subscribeMethod: "SubscribeJob",
    unsubscribeMethod: "UnsubscribeJob",
    subscribeArgs: [jobId],
    eventHandlers: events as Record<string, (...args: unknown[]) => void>,
    enabled: !!jobId,
  });
}

export const JOB_HUB_PATH = PATH;
