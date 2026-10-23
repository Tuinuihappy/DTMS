"use client";

import { useSyncExternalStore } from "react";

// Live `matchMedia` result. Server render and first client render report
// `serverValue` (false by default), so anything gated on it mounts after
// hydration — use it for decorative or heavy subtrees that should not be
// mounted at all (not merely `hidden`) when the query does not match.
export function useMediaQuery(query: string, serverValue = false): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const mql = window.matchMedia(query);
      mql.addEventListener("change", onChange);
      return () => mql.removeEventListener("change", onChange);
    },
    () => window.matchMedia(query).matches,
    () => serverValue,
  );
}
