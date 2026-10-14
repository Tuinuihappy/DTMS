// Every tunable of the operator app's in-app alerts, in one place
// (docs/plans/pool-new-trip-alert.md). The two the pure detection logic
// needs are defined there (it must stay import-free for node --test).

export { CHIME_COALESCE_MS, SEEN_CAP } from "./pool-alert-logic";

/** Pool refetch interval while the realtime feed is down. */
export const POOL_FALLBACK_POLL_MS = 30_000;

/** Don't refetch on every quick app switch. */
export const VISIBILITY_REFETCH_MIN_MS = 5_000;

export type ToneStep = { hz: number; ms: number };

/** Loud enough for a shop floor: square wave, three alternating steps. */
export const TONES: Record<"pool-new" | "trip-ended", ToneStep[]> = {
  "pool-new": [
    { hz: 1000, ms: 450 },
    { hz: 1400, ms: 450 },
    { hz: 1000, ms: 450 },
  ],
  // Lower and falling, so it doesn't sound like "new work".
  "trip-ended": [
    { hz: 660, ms: 400 },
    { hz: 440, ms: 400 },
    { hz: 330, ms: 600 },
  ],
};

export const TONE_GAIN = 0.8;

export const VIBRATION: Record<"pool-new" | "trip-ended", number[]> = {
  "pool-new": [300, 150, 300, 150, 300],
  "trip-ended": [600, 200, 600],
};

/** Per-device preferences (localStorage), read only in effects. */
export const PREF_KEYS = {
  sound: "operator:pool-alert",
  keepAwake: "operator:keep-awake",
  lastChimeAt: "operator:pool-alert:last-chime",
} as const;
