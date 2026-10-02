"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { usePollSchedule } from "@/lib/hooks/use-poll-schedule";

// Phase P3 — Reusable polling hook for projection-backed widgets that
// need to refresh on a cadence + on tab focus. Intentionally minimal:
// no global cache, no SWR-style mutation API. Caller owns the fetcher
// and the projection's freshness metadata is read off the response.
//
// Pattern:
//   const { data, loading, error, refresh, lastUpdated } =
//       useProjectionPoll(() => getOrderFunnel(window), { intervalMs: 10000 });
//
// Timing — interval, pausing while the tab is hidden, refetch on return,
// aborting a request in flight, and backing off on failure or a rate limit —
// belongs to usePollSchedule, shared with the screens that own their own state.

export type ProjectionPollState<T> = {
  data: T | null;
  loading: boolean;
  error: string | null;
  lastUpdated: Date | null;
  refresh: () => void;
};

export function useProjectionPoll<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  opts: {
    intervalMs?: number;
    enabled?: boolean;
    /** What the fetcher asks for (e.g. the report window). When it changes
     *  the old data is dropped and a fetch runs now — otherwise the new
     *  fetcher would only be picked up on the next interval tick. Must be
     *  stable between renders (a primitive or a memoised object). */
    resetKey?: unknown;
  } = {},
): ProjectionPollState<T> {
  const intervalMs = opts.intervalMs ?? 10_000;
  const enabled = opts.enabled ?? true;
  const resetKey = opts.resetKey;

  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  // Cache the fetcher in a ref so callers can recreate it inline
  // (`() => api(...)`) on every render without forcing a re-poll.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  const tick = useCallback(async (signal: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const result = await fetcherRef.current(signal);
      if (!signal.aborted) {
        setData(result);
        setLastUpdated(new Date());
      }
    } catch (e) {
      if (!signal.aborted) setError((e as Error).message);
      // Rethrown so the schedule backs off; an aborted request is not a
      // failure, it was replaced.
      if (!signal.aborted) throw e;
    } finally {
      if (!signal.aborted) setLoading(false);
    }
  }, []);

  const { trigger } = usePollSchedule(tick, { intervalMs, enabled });

  const refresh = useCallback(() => trigger(), [trigger]);

  // Numbers for the previous window must not stay on screen labelled as
  // the new one — clear to the loading skeleton, then fetch.
  const lastResetKeyRef = useRef(resetKey);
  useEffect(() => {
    if (Object.is(lastResetKeyRef.current, resetKey)) return;
    lastResetKeyRef.current = resetKey;
    setData(null);
    setLastUpdated(null);
    setLoading(true);
    trigger();
  }, [resetKey, trigger]);

  return { data, loading, error, lastUpdated, refresh };
}
