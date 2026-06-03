"use client";

import {
  MessageSquareDot,
  Radio,
  Route,
  Truck,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";

/* -------------------------------------------------------------------------- */
/* LandingIntro — fixed-overlay boot sequence shown on every entry to /.       */
/*                                                                            */
/* A miniature constellation comes online: truck mark scales in centre, three  */
/* pastel pills (Live freight / Smart dispatch / Driver comms) materialize at  */
/* 25/50/75% with their arcs fading in alongside, a big 0→100 counter and      */
/* progress bar climb together, then a "MOVE · DELIVER · ARRIVE" tagline       */
/* stagger-reveals near 90%. Hits 100 → holds 300ms → fades 450ms.             */
/*                                                                            */
/* Theme-aware via the existing canvas + pastel + ink tokens. Responsive via   */
/* a one-shot viewport-width measurement on mount (no need to watch resize     */
/* during a 1.5s overlay). prefers-reduced-motion fast-forwards to 100 and     */
/* dismisses in 200ms.                                                         */
/* -------------------------------------------------------------------------- */

const CLIMB_MS = 1500;
const HOLD_MS = 320;
const FADE_MS = 450;
const ease = [0.22, 1, 0.36, 1] as const;

// Three letters of the brand mark — stagger-revealed near the end of the climb
// using the same three thresholds the tagline used to drive.
const WORDS = ["T", "M", "S"] as const;

type PillSpec = {
  label: string;
  icon: typeof Truck;
  tone: "sky" | "peach" | "mint";
  angle: number;
  threshold: number; // % at which the pill + its arc start to appear
};

const PILLS: PillSpec[] = [
  { label: "Live freight", icon: Radio, tone: "sky", angle: -120, threshold: 25 },
  { label: "Smart dispatch", icon: Route, tone: "peach", angle: -40, threshold: 50 },
  { label: "Driver comms", icon: MessageSquareDot, tone: "mint", angle: 80, threshold: 75 },
];

export function LandingIntro() {
  const [progress, setProgress] = useState(0);
  const [visible, setVisible] = useState(true);
  // Compact (mobile) vs full (≥sm) sizing. Measured once on mount.
  const [compact, setCompact] = useState(false);

  useEffect(() => {
    setCompact(window.innerWidth < 640);

    // Respect prefers-reduced-motion — jump to 100 and dismiss immediately.
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setProgress(100);
      const t = window.setTimeout(() => setVisible(false), 200);
      return () => window.clearTimeout(t);
    }

    let raf = 0;
    let start: number | null = null;
    const tick = (now: number) => {
      if (start === null) start = now;
      const t = Math.min(1, (now - start) / CLIMB_MS);
      // Ease-out cubic — climb decelerates as it approaches 100 so the last
      // few % don't whip past the eye.
      const eased = 1 - Math.pow(1 - t, 3);
      setProgress(Math.round(eased * 100));
      if (t < 1) {
        raf = requestAnimationFrame(tick);
      } else {
        window.setTimeout(() => setVisible(false), HOLD_MS);
      }
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, []);

  const truckSize = compact ? 156 : 200;
  const pillRadius = compact ? 152 : 200;
  // Arc SVG sized exactly to the orbit + padding so the dashed curves end
  // precisely at the pill anchors (1 viewBox unit = 1 screen px).
  const arcSize = pillRadius * 2 + 80;

  return (
    <AnimatePresence>
      {visible && (
        <motion.div
          key="landing-intro"
          initial={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: FADE_MS / 1000, ease }}
          className="fixed inset-0 z-[60] flex items-center justify-center overflow-hidden bg-[var(--color-canvas)]"
          aria-hidden
        >
          <IntroAtmosphere />

          <div className="relative z-10 flex flex-col items-center px-6">
            <Constellation
              progress={progress}
              truckSize={truckSize}
              pillRadius={pillRadius}
              arcSize={arcSize}
            />
            <Counter progress={progress} />
            <ProgressBar progress={progress} compact={compact} />
            <Tagline progress={progress} />
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

/* -------------------------------------------------------------------------- */
/* Atmosphere — same pastel mesh + dot grid as the rest of the app.            */
/* -------------------------------------------------------------------------- */

function IntroAtmosphere() {
  return (
    <div aria-hidden className="pointer-events-none absolute inset-0 z-0">
      <div
        className="absolute inset-0 opacity-[0.85] dark:opacity-[0.55]"
        style={{
          background:
            "radial-gradient(900px 600px at 18% 18%, var(--color-pastel-peach), transparent 60%), radial-gradient(800px 700px at 82% 78%, var(--color-pastel-lavender), transparent 65%), radial-gradient(700px 600px at 60% 20%, var(--color-pastel-sky), transparent 65%)",
        }}
      />
      <svg
        className="absolute inset-0 h-full w-full text-[var(--color-ink-300)] opacity-[0.18] dark:opacity-0"
        preserveAspectRatio="none"
      >
        <defs>
          <pattern id="intro-dotgrid" width="22" height="22" patternUnits="userSpaceOnUse">
            <circle cx="1" cy="1" r="0.9" fill="currentColor" />
          </pattern>
        </defs>
        <rect width="100%" height="100%" fill="url(#intro-dotgrid)" />
      </svg>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Constellation — truck mark centre, three pills materialising at thresholds, */
/* arcs fading in toward each pill. Static positioning (the orbit is brief).   */
/* -------------------------------------------------------------------------- */

function Constellation({
  progress,
  truckSize,
  pillRadius,
  arcSize,
}: {
  progress: number;
  truckSize: number;
  pillRadius: number;
  arcSize: number;
}) {
  const half = arcSize / 2;

  return (
    <div
      className="relative grid place-items-center"
      style={{ width: arcSize, height: arcSize }}
    >
      {/* Arcs — fade in per pill threshold; pathLength stays at 1 (no draw-in
          animation here, the brief overlay benefits from instant reveal). */}
      <svg
        aria-hidden
        viewBox={`${-half} ${-half} ${arcSize} ${arcSize}`}
        className="absolute text-[var(--color-ink-400)]"
        style={{ width: arcSize, height: arcSize }}
        preserveAspectRatio="xMidYMid meet"
      >
        <defs>
          <linearGradient id="intro-arc-fade" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="currentColor" stopOpacity="0.6" />
            <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
          </linearGradient>
        </defs>
        {PILLS.map((p) => {
          const rad = (p.angle * Math.PI) / 180;
          const x = Math.cos(rad) * pillRadius;
          const y = Math.sin(rad) * pillRadius;
          const cx = x / 2 + Math.cos(rad + Math.PI / 2) * 22;
          const cy = y / 2 + Math.sin(rad + Math.PI / 2) * 22;
          // Reveal: pill threshold → +15% to fully drawn.
          const reveal = Math.min(1, Math.max(0, (progress - p.threshold) / 15));
          return (
            <path
              key={p.label}
              d={`M 0 0 Q ${cx} ${cy} ${x} ${y}`}
              fill="none"
              stroke="url(#intro-arc-fade)"
              strokeWidth="1.2"
              strokeDasharray="3 6"
              style={{ opacity: reveal * 0.55, transition: "opacity 0.18s linear" }}
            />
          );
        })}
      </svg>

      {/* Pills */}
      {PILLS.map((p) => {
        const rad = (p.angle * Math.PI) / 180;
        const x = Math.cos(rad) * pillRadius;
        const y = Math.sin(rad) * pillRadius;
        const shown = progress >= p.threshold;
        return (
          <div
            key={p.label}
            className="absolute"
            style={{
              left: `calc(50% + ${x}px)`,
              top: `calc(50% + ${y}px)`,
              transform: "translate(-50%, -50%)",
            }}
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.7, y: 6 }}
              animate={{
                opacity: shown ? 1 : 0,
                scale: shown ? 1 : 0.7,
                y: shown ? 0 : 6,
              }}
              transition={{ duration: 0.5, ease }}
            >
              <Pill tone={p.tone} icon={p.icon} label={p.label} />
            </motion.div>
          </div>
        );
      })}

      {/* Truck mark — same rainbow-conic squircle as login + landing hero. */}
      <motion.div
        initial={{ opacity: 0, scale: 0.7, rotate: -16 }}
        animate={{ opacity: 1, scale: 1, rotate: -8 }}
        transition={{ duration: 0.85, delay: 0.05, ease }}
        className="relative"
      >
        <motion.div
          animate={{ y: [0, -4, 0] }}
          transition={{ duration: 5, ease: "easeInOut", repeat: Infinity }}
          className="grid place-items-center rounded-[28%] text-white"
          style={{
            width: truckSize,
            height: truckSize,
            background:
              "conic-gradient(from 45deg, #c7d4ff, #e0d4ff, #ffd4e6, #ffe7c7, #d4f0ff, #c7d4ff)",
            boxShadow:
              "inset 0 6px 18px rgba(255,255,255,0.95), inset 0 -8px 24px rgba(79,93,255,0.18), 0 22px 60px -18px rgba(79,93,255,0.55), 0 50px 90px -30px rgba(14,21,48,0.35)",
          }}
        >
          <Truck
            className="h-[42%] w-[42%] text-[var(--color-brand-900)]"
            strokeWidth={2.1}
            style={{ transform: "rotate(8deg)" }}
          />
        </motion.div>
      </motion.div>
    </div>
  );
}

function Pill({
  tone,
  icon: Icon,
  label,
}: {
  tone: "sky" | "peach" | "mint";
  icon: typeof Truck;
  label: string;
}) {
  const bg = `var(--color-pastel-${tone})`;
  const ink = `var(--color-pastel-${tone}-ink)`;
  return (
    <span
      className="glass glass-edge inline-flex items-center gap-2 rounded-full px-3 py-2 backdrop-blur"
      style={{ background: bg, borderColor: "rgba(255,255,255,0.5)" }}
    >
      <span
        className="grid h-6 w-6 place-items-center rounded-full"
        style={{ background: "rgba(255,255,255,0.65)", color: ink }}
      >
        <Icon className="h-3 w-3" strokeWidth={2.2} />
      </span>
      <span
        className="text-[11.5px] font-semibold tracking-tight"
        style={{ color: ink }}
      >
        {label}
      </span>
    </span>
  );
}

/* -------------------------------------------------------------------------- */
/* Counter — the big 0–100 number. tabular-nums so digits don't jump width.    */
/* -------------------------------------------------------------------------- */

function Counter({ progress }: { progress: number }) {
  return (
    <div
      className="mt-10 flex items-start gap-1.5 font-display font-medium leading-none tracking-[-0.04em] tabular-nums text-[var(--color-ink-900)]"
      aria-live="polite"
      aria-label={`Loading ${progress} percent`}
    >
      <span className="text-[3.25rem] sm:text-[4.5rem]">{progress}</span>
      <span className="mt-1.5 text-[1.5rem] sm:text-[2rem] text-[var(--color-ink-400)]">
        %
      </span>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* ProgressBar — thin brand-gradient fill, 240/300px wide.                    */
/* -------------------------------------------------------------------------- */

function ProgressBar({
  progress,
  compact,
}: {
  progress: number;
  compact: boolean;
}) {
  return (
    <div
      className="mt-5 h-[3px] overflow-hidden rounded-full bg-[var(--color-ink-100)] dark:bg-white/[0.08]"
      style={{ width: compact ? 240 : 300 }}
    >
      <div
        className="h-full rounded-full"
        style={{
          width: `${progress}%`,
          background:
            "linear-gradient(90deg, var(--color-brand-500), var(--color-brand-900))",
          transition: "width 0.16s ease-out",
          boxShadow: "0 0 12px -2px var(--color-brand-500)",
        }}
      />
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* Tagline — three uppercase words appear one by one near the end.            */
/* -------------------------------------------------------------------------- */

function Tagline({ progress }: { progress: number }) {
  const thresholds = [80, 88, 95];
  return (
    <div className="mt-6 flex items-center justify-center gap-x-4 font-display text-[15px] font-semibold uppercase text-[var(--color-ink-500)] sm:text-[17px]">
      {WORDS.map((w, i) => {
        const shown = progress >= thresholds[i];
        return (
          <motion.span
            key={w}
            initial={{ opacity: 0, y: 6 }}
            animate={{ opacity: shown ? 1 : 0, y: shown ? 0 : 6 }}
            transition={{ duration: 0.35, ease }}
          >
            {w}
          </motion.span>
        );
      })}
    </div>
  );
}
