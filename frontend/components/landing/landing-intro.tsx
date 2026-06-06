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
/* progress bar climb together, then T · M · S brand letters stagger-reveal    */
/* near the end. Hits 100 → holds 320ms → fades 450ms.                         */
/*                                                                            */
/* Theme-aware via the existing canvas + pastel + ink tokens.                  */
/*                                                                            */
/* Fully responsive: a single `scale` factor derived from viewport min-dim     */
/* drives every size (truck, sparkle, pill radii, arc SVG, pill chip via       */
/* transform, counter font, bar width, tagline font). Scale = clamp(0.55,     */
/* min(w/1024, h/768), 1.2) — treats 1024×768 (iPad landscape) as the          */
/* design reference. Resize listener keeps everything in sync.                 */
/*                                                                            */
/* prefers-reduced-motion fast-forwards to 100 and dismisses in 200ms.         */
/* -------------------------------------------------------------------------- */

const CLIMB_MS = 1500;
const HOLD_MS = 320;
const FADE_MS = 450;
const ease = [0.22, 1, 0.36, 1] as const;

// Three letters of the brand mark — stagger-revealed near the end of the climb.
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

/* Responsive sizing — every visual element derives from a single scale factor
   so the composition stays proportional from narrow phones up to ultra-wide
   monitors. The reference design lives at 1280×900 (laptop) → scale = 1;
   tablets land naturally at 0.8–0.95 with comfortable breathing room above
   and below the constellation so the overlay never crowds the viewport edges
   (especially under iOS Safari's URL bar). */
const REF_WIDTH = 1280;
const REF_HEIGHT = 900;
const MIN_SCALE = 0.55;
const MAX_SCALE = 1.0;
const TRUCK_BASE = 200;
const PILL_RADIUS_BASE = 200;
const ARC_PAD_BASE = 80;
const BAR_WIDTH_BASE = 300;
const COUNTER_FONT_BASE = 4.5; // rem
const PERCENT_FONT_BASE = 2;   // rem
const TAGLINE_FONT_BASE = 17;  // px

function computeScale() {
  if (typeof window === "undefined") return 1;
  const byWidth = window.innerWidth / REF_WIDTH;
  const byHeight = window.innerHeight / REF_HEIGHT;
  return Math.min(MAX_SCALE, Math.max(MIN_SCALE, Math.min(byWidth, byHeight)));
}

export function LandingIntro() {
  const [progress, setProgress] = useState(0);
  const [visible, setVisible] = useState(true);
  // Lazy initializer reads window if available (client). Server gets 1 →
  // hydration is identical on desktop; on mobile a single-frame correction
  // happens once useEffect runs.
  const [scale, setScale] = useState(computeScale);
  // `mounted` gates the constellation column with opacity 0 → 1 so the
  // viewport-correct scale is in place BEFORE the user sees anything. Kills
  // the SSR-vs-client size jolt (server renders scale=1 → client recomputes
  // → layout shifted big→small on first paint).
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    const updateScale = () => setScale(computeScale());
    updateScale();
    setMounted(true);
    window.addEventListener("resize", updateScale);

    // Respect prefers-reduced-motion — jump to 100 and dismiss immediately.
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setProgress(100);
      const t = window.setTimeout(() => setVisible(false), 200);
      return () => {
        window.clearTimeout(t);
        window.removeEventListener("resize", updateScale);
      };
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
    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener("resize", updateScale);
    };
  }, []);

  const truckSize = Math.round(TRUCK_BASE * scale);
  const pillRadius = Math.round(PILL_RADIUS_BASE * scale);
  const arcSize = Math.round(pillRadius * 2 + ARC_PAD_BASE * scale);
  const barWidth = Math.round(BAR_WIDTH_BASE * scale);
  // Counter / tagline floor at a readable minimum so very narrow viewports
  // don't render unreadable type even at MIN_SCALE.
  const counterFontRem = Math.max(2.4, COUNTER_FONT_BASE * scale);
  const percentFontRem = Math.max(1.2, PERCENT_FONT_BASE * scale);
  const taglineFontPx = Math.max(12, TAGLINE_FONT_BASE * scale);

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

          <div
            className="relative z-10 flex flex-col items-center px-6"
            style={{
              opacity: mounted ? 1 : 0,
              transition: "opacity 220ms ease",
            }}
          >
            <Constellation
              progress={progress}
              scale={scale}
              truckSize={truckSize}
              pillRadius={pillRadius}
              arcSize={arcSize}
            />
            <Counter
              progress={progress}
              counterFontRem={counterFontRem}
              percentFontRem={percentFontRem}
            />
            <ProgressBar progress={progress} width={barWidth} />
            <Tagline progress={progress} fontSizePx={taglineFontPx} />
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
/* arcs fading in toward each pill. All sizes derive from the shared scale.    */
/* -------------------------------------------------------------------------- */

function Constellation({
  progress,
  scale,
  truckSize,
  pillRadius,
  arcSize,
}: {
  progress: number;
  scale: number;
  truckSize: number;
  pillRadius: number;
  arcSize: number;
}) {
  const half = arcSize / 2;
  // Bow control offset scales with the orbit too so arcs keep their curvature.
  const ctrlOffset = 22 * scale;

  return (
    <div
      className="relative grid place-items-center"
      style={{ width: arcSize, height: arcSize }}
    >
      {/* Arcs — viewBox renders 1:1 with px size so endpoints land exactly on
          each pill anchor at every scale. */}
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
          const cx = x / 2 + Math.cos(rad + Math.PI / 2) * ctrlOffset;
          const cy = y / 2 + Math.sin(rad + Math.PI / 2) * ctrlOffset;
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

      {/* Pills — outer div positions on the anchor + scales the chip; inner
          motion.div carries the entrance animation. */}
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
              transform: `translate(-50%, -50%) scale(${scale})`,
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
/* Font size scales with the page (floored to stay readable on phones).        */
/* -------------------------------------------------------------------------- */

function Counter({
  progress,
  counterFontRem,
  percentFontRem,
}: {
  progress: number;
  counterFontRem: number;
  percentFontRem: number;
}) {
  return (
    <div
      className="mt-10 flex items-start gap-1.5 font-display font-medium leading-none tracking-[-0.04em] tabular-nums text-[var(--color-ink-900)]"
      aria-live="polite"
      aria-label={`Loading ${progress} percent`}
    >
      <span style={{ fontSize: `${counterFontRem}rem` }}>{progress}</span>
      <span
        className="mt-1.5 text-[var(--color-ink-400)]"
        style={{ fontSize: `${percentFontRem}rem` }}
      >
        %
      </span>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* ProgressBar — thin brand-gradient fill, width scales with viewport.        */
/* -------------------------------------------------------------------------- */

function ProgressBar({
  progress,
  width,
}: {
  progress: number;
  width: number;
}) {
  return (
    <div
      className="mt-5 h-[3px] overflow-hidden rounded-full bg-[var(--color-ink-100)] dark:bg-white/[0.08]"
      style={{ width }}
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
/* Tagline — T · M · S brand letters appear one by one near the end.          */
/* Font scales with the page (floored at 12px for legibility).                 */
/* -------------------------------------------------------------------------- */

function Tagline({
  progress,
  fontSizePx,
}: {
  progress: number;
  fontSizePx: number;
}) {
  const thresholds = [80, 88, 95];
  return (
    <div
      className="mt-6 flex items-center justify-center gap-x-4 font-display font-semibold uppercase text-[var(--color-ink-500)]"
      style={{ fontSize: `${fontSizePx}px` }}
    >
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
