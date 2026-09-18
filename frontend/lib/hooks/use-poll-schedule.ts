"use client";

import { useCallback, useEffect, useRef } from "react";
import { RateLimitError } from "@/lib/api/errors";

// The timing half of polling, shared by every screen that refreshes on a
// cadence. The caller owns its own data and state; this owns *when*.
//
// Behaviour:
//   - Runs on mount, then every `intervalMs`.
//   - Pauses while the tab is hidden, and runs once when it comes back.
//   - Aborts a request still in flight before starting the next.
//   - Backs off on failure, and by exactly what the server asked for when it
//     is a rate limit. Without that, a refused poll keeps asking at the same
//     rate and turns one 429 into a stream of them.
//   - `trigger()` (used by SignalR events) respects the back-off: while one is
//     running it schedules the next run instead of firing immediately.

/** Doubling per consecutive failure, capped so a screen left open overnight
 *  still recovers on its own. */
const MAX_BACKOFF_MS = 5 * 60_000;

export type PollSchedule = {
  /** Run now, or as soon as an active back-off allows. */
  trigger: () => void;
};

export function usePollSchedule(
  tick: (signal: AbortSignal) => Promise<void>,
  opts: { intervalMs: number; enabled?: boolean },
): PollSchedule {
  const { intervalMs, enabled = true } = opts;

  // Held in a ref so a caller can pass an inline closure without restarting
  // the schedule on every render.
  const tickRef = useRef(tick);
  tickRef.current = tick;

  const abortRef = useRef<AbortController | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const failuresRef = useRef(0);
  const blockedUntilRef = useRef(0);
  const runRef = useRef<(() => void) | null>(null);

  useEffect(() => {
    if (!enabled) return;

    let cancelled = false;

    const clearTimer = () => {
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = null;
    };

    const schedule = (delayMs: number) => {
      if (cancelled) return;
      clearTimer();
      timerRef.current = setTimeout(run, delayMs);
    };

    function run() {
      if (cancelled) return;

      // A hidden tab stops asking; the visibility handler runs it on return.
      if (typeof document !== "undefined" && document.hidden) {
        schedule(intervalMs);
        return;
      }

      const wait = blockedUntilRef.current - Date.now();
      if (wait > 0) {
        schedule(wait);
        return;
      }

      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      void tickRef.current(controller.signal).then(
        () => {
          if (cancelled || controller.signal.aborted) return;
          failuresRef.current = 0;
          blockedUntilRef.current = 0;
          schedule(intervalMs);
        },
        (error: unknown) => {
          if (cancelled || controller.signal.aborted) return;
          failuresRef.current += 1;

          // The server's own wait wins over any guess of ours.
          const delay =
            error instanceof RateLimitError
              ? Math.max(error.retryAfterSeconds * 1000, intervalMs)
              : Math.min(intervalMs * 2 ** failuresRef.current, MAX_BACKOFF_MS);

          blockedUntilRef.current = Date.now() + delay;
          schedule(delay);
        },
      );
    }

    runRef.current = run;
    run();

    const onVisibility = () => {
      if (typeof document !== "undefined" && !document.hidden) run();
    };
    if (typeof document !== "undefined") {
      document.addEventListener("visibilitychange", onVisibility);
    }

    return () => {
      cancelled = true;
      runRef.current = null;
      clearTimer();
      if (typeof document !== "undefined") {
        document.removeEventListener("visibilitychange", onVisibility);
      }
      abortRef.current?.abort();
    };
  }, [intervalMs, enabled]);

  // Stable across renders so callers can hand it straight to a hub
  // subscription without re-subscribing.
  const trigger = useCallback(() => runRef.current?.(), []);

  return { trigger };
}
