"use client";

import { useEffect, useRef } from "react";
import {
  motion,
  useInView,
  useMotionValue,
  useReducedMotion,
  useSpring,
  useTransform,
} from "motion/react";

type Props = {
  value: number;
  decimals?: number;
  prefix?: string;
  suffix?: string;
  className?: string;
  /** "zero" (default): count up from 0 on first view — showcase tiles.
   *  "value": render the real number at once and only roll on later
   *  changes — live counts an operator reads (orders total). */
  from?: "zero" | "value";
};

// Counts up to `value` once scrolled into view, then glides between later
// values from wherever it is (the spring carries on from its current
// position — no restart from 0).
//
// The text is a motion value rendered as the span's child, so motion writes
// it straight to the DOM each frame; React renders once per `value` change,
// not once per spring frame. tabular-nums keeps the width steady while the
// digits roll. useSpring isn't covered by <MotionConfig reducedMotion>, so
// reduced motion is handled here: jump straight to the value.
export function NumberTicker({ value, decimals = 0, prefix = "", suffix = "", className, from = "zero" }: Props) {
  const ref = useRef<HTMLSpanElement>(null);
  const inView = useInView(ref, { once: true, margin: "-20%" });
  const reduced = useReducedMotion();
  // Initial value only — later changes go through the effect below.
  const target = useMotionValue(from === "value" ? value : 0);
  const spring = useSpring(target, { stiffness: 150, damping: 25 });
  const text = useTransform(
    spring,
    (v) =>
      prefix +
      v.toLocaleString("en-US", {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals,
      }) +
      suffix,
  );

  useEffect(() => {
    if (!inView) return;
    if (reduced) {
      target.jump(value);
      spring.jump(value);
    } else {
      target.set(value);
    }
  }, [inView, value, reduced, target, spring]);

  return (
    <motion.span ref={ref} className={`tabular-nums ${className ?? ""}`}>
      {text}
    </motion.span>
  );
}
