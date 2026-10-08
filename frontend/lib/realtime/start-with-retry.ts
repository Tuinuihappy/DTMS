// Start a SignalR connection, retrying until it is up.
//
// SignalR's withAutomaticReconnect only covers a connection that was up
// and then dropped. A start() that fails — API down at page load, hub-token
// 401, a drain-cycle restart landing in the server's settle window — leaves
// the connection Disconnected for good, and every page using that hub stays
// silent until a reload (frontend review M15). This loop is what retries.
//
// No imports on purpose: `node --test` runs the sibling test file directly,
// and it can't resolve the "@/" alias or the SignalR package.

export type HubLinkState = "connected" | "disconnected" | "busy";

/** Same curve as the reconnect policy: 1s → 2s → 4s … capped at 30s, plus
 *  up to 1s of jitter so a fleet of tablets doesn't retry in lockstep. */
export function retryDelayMs(attempt: number, jitter: number): number {
  return Math.min(1_000 * 2 ** attempt, 30_000) + jitter * 1_000;
}

export type StartWithRetryOptions = {
  start: () => Promise<void>;
  /** "busy" = connecting/reconnecting/disconnecting — someone else is
   *  already moving it; wait rather than start a second time. */
  state: () => HubLinkState;
  /** Checked every round; true ends the loop without connecting. */
  shouldStop?: () => boolean;
  sleep: (ms: number) => Promise<void>;
  random: () => number;
  onError?: (err: unknown, attempt: number) => void;
};

/** Resolves once the connection is connected (by this loop or anyone
 *  else), or when shouldStop() says so. Never rejects. */
export async function startWithRetry(opts: StartWithRetryOptions): Promise<void> {
  const { start, state, shouldStop, sleep, random, onError } = opts;
  let attempt = 0;
  for (;;) {
    if (shouldStop?.()) return;
    const s = state();
    if (s === "connected") return;
    if (s === "disconnected") {
      try {
        await start();
        return;
      } catch (err) {
        onError?.(err, attempt);
      }
      await sleep(retryDelayMs(attempt, random()));
      attempt++;
    } else {
      // Another start or reconnect is in flight — check back shortly.
      await sleep(500);
    }
  }
}
