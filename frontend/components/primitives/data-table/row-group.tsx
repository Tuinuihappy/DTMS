"use client";

import { useReducedMotion } from "motion/react";
import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { dur } from "@/lib/motion";

// Whether rows under this group should play their own entrance. True by
// default so tables that don't wrap their rows in a RowGroup keep the old
// behaviour (a short per-row fade on mount).
const RowEntranceContext = createContext(true);

export function useRowEntrance(): boolean {
  return useContext(RowEntranceContext);
}

/**
 * Container for a list of DataRow / MobileCardRow that owns the list's
 * motion, so rows don't each choreograph themselves on every refetch.
 *
 * - First load: rows play their short staggered entrance.
 * - `signature` changes (page / sort / filters — the query that produced
 *   the rows on screen): the whole group fades in once, quickly; rows
 *   mount still. Per-row stagger on every page click read as the table
 *   loading twice.
 * - Same signature, new data (poll / realtime): nothing moves here —
 *   pair with useFlashOnChange to point at what changed.
 *
 * Pass the signature of the data actually rendered, not of the request
 * in flight, or the fade plays on the old rows before the new ones land.
 */
export function RowGroup({
  signature,
  as = "tbody",
  className,
  children,
}: {
  signature: string;
  as?: "tbody" | "div";
  className?: string;
  children: ReactNode;
}) {
  // Adjust-state-on-prop-change: once the query changes, rows stop doing
  // their own entrance for the rest of this group's life.
  const [prevSig, setPrevSig] = useState(signature);
  const [entrance, setEntrance] = useState(true);
  if (signature !== prevSig) {
    setPrevSig(signature);
    if (entrance) setEntrance(false);
  }

  const reduce = useReducedMotion();
  const ref = useRef<HTMLElement | null>(null);
  const animatedSigRef = useRef(signature);
  useEffect(() => {
    if (animatedSigRef.current === signature) return;
    animatedSigRef.current = signature;
    // WAAPI, not a motion component: MotionConfig's reducedMotion doesn't
    // reach it, hence the explicit check.
    if (reduce) return;
    ref.current?.animate([{ opacity: 0.2 }, { opacity: 1 }], {
      duration: dur.fast * 1000,
      easing: "cubic-bezier(0.22, 1, 0.36, 1)",
    });
  }, [signature, reduce]);

  const body =
    as === "tbody" ? (
      <tbody ref={(el) => { ref.current = el; }} className={className}>
        {children}
      </tbody>
    ) : (
      <div ref={(el) => { ref.current = el; }} className={className}>
        {children}
      </div>
    );

  return <RowEntranceContext.Provider value={entrance}>{body}</RowEntranceContext.Provider>;
}
