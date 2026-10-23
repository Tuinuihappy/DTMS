"use client";

import { useEffect, useState } from "react";

// Pages whose entrance choreography has already played in this tab. Module
// state, so it survives client-side navigation and resets on a full reload.
const played = new Set<string>();

/**
 * True on the first mount of `page` in this session, false after. Use it to
 * gate entrance animations — `initial={first ? {...} : false}` — so an
 * operator bouncing back to the dashboard isn't made to watch the cards
 * fly in again. Every component on the page reads `true` during that first
 * render; the flag is only recorded after commit.
 */
export function useFirstVisit(page: string): boolean {
  const [first] = useState(() => !played.has(page));
  useEffect(() => {
    played.add(page);
  }, [page]);
  return first;
}
