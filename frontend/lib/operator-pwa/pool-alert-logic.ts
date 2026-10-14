// Decides when a change to the pool list means "new work — alert".
//
// Driven by the list itself, not by where the change came from: a SignalR
// PoolTripAdded, the fallback poll, the refetch on screen-on and the one on
// reconnect all just produce a new `trips` array, and this compares it with
// what was already seen. One rule, so no path can double-chime or be missed.
//
// `seen` is kept apart from the list on purpose: a refetch that fails or
// briefly returns fewer trips must not make the old ones "new" again.
//
// No imports: node --test runs the test file directly, and a ".ts" import
// here would need allowImportingTsExtensions project-wide. The two
// constants live here and alert-config.ts re-exports them.

/** New pool trips arriving within this window share one chime. */
export const CHIME_COALESCE_MS = 3_000;

/** How many trip ids the "already seen" memory keeps (a shift's worth). */
export const SEEN_CAP = 500;

export type PoolWatch = {
  /** False until the first successful load; that load is never "new". */
  primed: boolean;
  /** Trip ids already seen, oldest first, capped at SEEN_CAP. */
  seen: readonly string[];
  lastChimeAt: number | null;
};

export const initialPoolWatch: PoolWatch = { primed: false, seen: [], lastChimeAt: null };

export type PoolObservation = {
  watch: PoolWatch;
  /** Ids in this list that were never seen before. */
  newIds: string[];
  /** Play the sound: there are new ids and no chime in the last window. */
  chime: boolean;
};

export function observePool(watch: PoolWatch, tripIds: readonly string[], now: number): PoolObservation {
  if (!watch.primed) {
    // Whatever is waiting when the app opens is not new — show the count,
    // stay quiet.
    return {
      watch: { primed: true, seen: cap(dedupe(tripIds)), lastChimeAt: watch.lastChimeAt },
      newIds: [],
      chime: false,
    };
  }

  const known = new Set(watch.seen);
  const newIds = dedupe(tripIds).filter((id) => !known.has(id));
  if (newIds.length === 0) return { watch, newIds, chime: false };

  const chime = watch.lastChimeAt === null || now - watch.lastChimeAt >= CHIME_COALESCE_MS;
  return {
    watch: {
      primed: true,
      seen: cap([...watch.seen, ...newIds]),
      lastChimeAt: chime ? now : watch.lastChimeAt,
    },
    newIds,
    chime,
  };
}

function dedupe(ids: readonly string[]): string[] {
  return [...new Set(ids)];
}

function cap(ids: string[]): string[] {
  return ids.length > SEEN_CAP ? ids.slice(ids.length - SEEN_CAP) : ids;
}
