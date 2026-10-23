"use client";

import { useEffect, useRef, useState } from "react";

const EMPTY: ReadonlySet<string> = new Set();

/**
 * Ids of rows that changed (or appeared) on a refresh of the SAME query —
 * a poll or realtime hint — so the table can paint `.row-flash` on them.
 *
 * `signature` is the page/sort/filter key of the rows passed in. When it
 * changes the operator navigated: everything on screen is "new", so
 * nothing flashes. An id stays in the set for `holdMs`, a little past the
 * CSS animation, then drops so the class comes off.
 *
 * `getId` / `getVersion` may be inline lambdas; the diff only runs when
 * `rows` or `signature` actually change.
 */
export function useFlashOnChange<T>(
  rows: readonly T[],
  getId: (row: T) => string,
  getVersion: (row: T) => string,
  signature: string,
  holdMs = 1500,
): ReadonlySet<string> {
  const [flashing, setFlashing] = useState<ReadonlySet<string>>(EMPTY);
  const prevRowsRef = useRef<readonly T[] | null>(null);
  const prevSigRef = useRef<string | null>(null);
  const prevVersionsRef = useRef<Map<string, string>>(new Map());
  const timersRef = useRef<Set<ReturnType<typeof setTimeout>>>(new Set());

  useEffect(() => {
    if (prevRowsRef.current === rows && prevSigRef.current === signature) return;
    const sameQuery = prevSigRef.current === signature && prevRowsRef.current !== null;
    const prev = prevVersionsRef.current;
    const next = new Map<string, string>();
    const changed: string[] = [];
    for (const row of rows) {
      const id = getId(row);
      const v = getVersion(row);
      next.set(id, v);
      if (sameQuery && prev.get(id) !== v) changed.push(id);
    }
    prevRowsRef.current = rows;
    prevSigRef.current = signature;
    prevVersionsRef.current = next;
    if (changed.length === 0) return;

    setFlashing((cur) => {
      const s = new Set(cur);
      for (const id of changed) s.add(id);
      return s;
    });
    const timers = timersRef.current;
    const t = setTimeout(() => {
      timers.delete(t);
      setFlashing((cur) => {
        const s = new Set(cur);
        for (const id of changed) s.delete(id);
        return s.size === 0 ? EMPTY : s;
      });
    }, holdMs);
    timers.add(t);
  }, [rows, signature, getId, getVersion, holdMs]);

  useEffect(() => {
    const timers = timersRef.current;
    return () => {
      for (const t of timers) clearTimeout(t);
      timers.clear();
    };
  }, []);

  return flashing;
}
