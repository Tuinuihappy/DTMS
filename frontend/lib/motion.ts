// Shared motion tokens. One place for durations, easings and springs so
// the app moves with one rhythm — and so "make it snappier" is one edit,
// not 243.
//
// Budget for interactive UI (anything the operator clicks, all day):
// enter ≤ 0.28s, exit ≤ 0.12s, total stagger ≤ 0.15s. Longer choreography
// is for the showcase pages only (landing, login brand scene, profile).
//
// Reduced motion: <MotionConfig reducedMotion="user"> in app/providers.tsx
// turns transform/layout animation off for every motion component. Things
// it does NOT reach — useSpring, hand-written rAF, attribute writes — must
// check useReducedMotion() themselves.

import type { Transition, Variants } from "motion/react";

export const dur = {
  instant: 0.12,
  fast: 0.18,
  base: 0.24,
  slow: 0.4,
} as const;

export const ease = {
  /** Default enter — quick start, long soft landing. */
  out: [0.22, 1, 0.36, 1],
  /** Exits — accelerate away. */
  in: [0.4, 0, 1, 1],
  inOut: [0.65, 0, 0.35, 1],
} as const;

export const spring = {
  /** Menus, popovers, pills. */
  snappy: { type: "spring", stiffness: 460, damping: 32 },
  /** Dialogs, toasts. */
  dialog: { type: "spring", stiffness: 360, damping: 30 },
  /** Side drawers. */
  drawer: { type: "spring", stiffness: 320, damping: 34, mass: 0.7 },
  /** Size changes — accordions, rail width. */
  gentle: { type: "spring", stiffness: 260, damping: 30, mass: 0.6 },
} satisfies Record<string, Transition>;

export const T = {
  fast: { duration: dur.fast, ease: ease.out },
  base: { duration: dur.base, ease: ease.out },
  slow: { duration: dur.slow, ease: ease.out },
  exit: { duration: dur.instant, ease: ease.in },
} satisfies Record<string, Transition>;

/** Per-item stagger delay, capped so long lists never wait on the tail. */
export const STAGGER_STEP = 0.025;
export const STAGGER_CAP = 0.15;
export function stagger(i: number, step = STAGGER_STEP, cap = STAGGER_CAP): number {
  return Math.min(i * step, cap);
}

/** `transition` for the i-th item of an entering list. */
export function listItem(i: number): Transition {
  return { ...T.base, delay: stagger(i) };
}

export const fade: Variants = {
  hidden: { opacity: 0 },
  shown: { opacity: 1, transition: T.base },
  exit: { opacity: 0, transition: T.exit },
};

export const rise: Variants = {
  hidden: { opacity: 0, y: 6 },
  shown: { opacity: 1, y: 0, transition: T.base },
  exit: { opacity: 0, transition: T.exit },
};

export const pop: Variants = {
  hidden: { opacity: 0, scale: 0.96, y: -4 },
  shown: { opacity: 1, scale: 1, y: 0, transition: spring.snappy },
  exit: { opacity: 0, scale: 0.97, transition: T.exit },
};
