"use client";

import { useCallback, useEffect, useRef, useState } from "react";

// Phase P3 — Reusable polling hook for projection-backed widgets that
// need to refresh on a cadence + on tab focus. Intentionally minimal:
// no global cache, no SWR-style mutation API. Caller owns the fetcher
// and the projection's freshness metadata is read off the response.
//
// Pattern:
//   const { data, loading, error, refresh, lastUpdated } =
//       useProjectionPoll(() => getOrderFunnel(window), { intervalMs: 10000 });
//
// Behavior:
//   - Initial fetch on mount.
//   - Polls every `intervalMs` (default 10s); pauses while document is hidden.
//   - Auto-refetches on visibility change → focus.
//   - Manual `refresh()` triggers an immediate fetch and resets the poll.
//   - In-flight requests are aborted on dep change so a slow response
//     doesn't overwrite a newer one.

export type ProjectionPollState<T> = {
  data: T | null;
  loading: boolean;
  error: string | null;
  lastUpdated: Date | null;
  refresh: () => void;
};

export function useProjectionPoll<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  opts: { intervalMs?: number; enabled?: boolean } = {},
): ProjectionPollState<T> {
  const intervalMs = opts.intervalMs ?? 10_000;
  const enabled = opts.enabled ?? true;

  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  // Cache the fetcher in a ref so callers can recreate it inline
  // (`() => api(...)`) on every render without forcing a re-poll.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  // Used by both the polling timer and the manual refresh button to
  // share one in-flight abortable request slot.
  const abortRef = useRef<AbortController | null>(null);

  const run = useCallback(async () => {
    abortRef.current?.abort();
    const ctl = new AbortController();
    abortRef.current = ctl;

    setLoading(true);
    setError(null);
    try {
      const result = await fetcherRef.current(ctl.signal);
      if (!ctl.signal.aborted) {
        setData(result);
        setLastUpdated(new Date());
      }
    } catch (e) {
      if (!ctl.signal.aborted) {
        setError((e as Error).message);
      }
    } finally {
      if (!ctl.signal.aborted) setLoading(false);
    }
  }, []);

  // Initial + interval polling. The dep array intentionally excludes
  // `run` (stable via useCallback) and includes only the inputs that
  // should reset the timer.
  useEffect(() => {
    if (!enabled) return;
    let timer: ReturnType<typeof setInterval> | null = null;
    let cancelled = false;

    const tick = () => {
      if (cancelled) return;
      if (typeof document !== "undefined" && document.hidden) return;
      void run();
    };

    void run();
    timer = setInterval(tick, intervalMs);

    const onVisibility = () => {
      if (typeof document !== "undefined" && !document.hidden) tick();
    };
    if (typeof document !== "undefined") {
      document.addEventListener("visibilitychange", onVisibility);
    }

    return () => {
      cancelled = true;
      if (timer) clearInterval(timer);
      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", onVisibility);
      }
      abortRef.current?.abort();
    };
  }, [intervalMs, enabled, run]);

  const refresh = useCallback(() => {
    void run();
  }, [run]);

  return { data, loading, error, lastUpdated, refresh };
}
