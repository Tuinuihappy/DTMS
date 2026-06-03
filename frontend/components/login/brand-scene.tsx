"use client";

import {
  MessageSquareDot,
  Radio,
  Route,
  Truck,
} from "lucide-react";
import { motion } from "motion/react";
import { useEffect, useRef, useState } from "react";

/* -------------------------------------------------------------------------- */
/* BrandScene — the left half of /login.                                       */
/*                                                                            */
/* Theme-aware by design: every color flows through existing CSS tokens.       */
/*                                                                            */
/* Responsive constellation: a ResizeObserver on the column measures the      */
/* actual available width and computes a scale factor. The truck mark, the    */
/* sparkle halo, the pill orbit radii, and the arc SVG dimensions all derive  */
/* from that scale, so the composition stays proportional from iPad mini      */
/* landscape (1024px) up through ultra-wide displays.                          */
/*                                                                            */
/* Layered (back to front):                                                    */
/*   1. Pastel mesh (atmosphere)                                               */
/*   2. Dotted chart grid (light theme only)                                   */
/*   3. Constellation backdrop — truck mark + orbiting pills + dashed arcs    */
/*   4. Text content — eyebrow / "Move. Deliver. Arrive." / paragraph         */
/*   5. Trust footnote                                                         */
/* -------------------------------------------------------------------------- */

const ease = [0.22, 1, 0.36, 1] as const;

const WORDS = ["Move.", "Deliver.", "Arrive."] as const;

type PillSpec = {
  label: string;
  icon: typeof Truck;
  tone: "sky" | "peach" | "mint";
  angle: number;  // degrees, 0° = right, 90° = down
  radius: number; // px from column centre at scale=1
  delay: number;  // motion entrance delay
};

const PILLS: PillSpec[] = [
  {
    label: "Live freight",
    icon: Radio,
    tone: "sky",
    angle: -120,
    radius: 290,
    delay: 0.95,
  },
  {
    label: "Smart dispatch",
    icon: Route,
    tone: "peach",
    angle: -40,
    radius: 310,
    delay: 1.05,
  },
  {
    label: "Driver comms",
    icon: MessageSquareDot,
    tone: "mint",
    angle: 80,
    radius: 300,
    delay: 1.15,
  },
];

/* Responsive sizing constants — the constellation is designed against a
   reference column width of 720px (xl viewport, 1.15fr:1fr grid). Anything
   wider gets a slightly bigger constellation up to MAX_SCALE; anything
   narrower shrinks down to MIN_SCALE so iPad mini landscape (1024px → col
   ~525px → scale ~0.73) still fits the orbit inside the column. */
const REF_COLUMN_WIDTH = 720;
const MIN_SCALE = 0.55;
const MAX_SCALE = 1.2;
const TRUCK_BASE = 300;
const SPARKLE_BASE = 320;
const ARC_PADDING = 80; // extra room around the orbit so dashes don't clip

export function BrandScene() {
  const containerRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(1);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    const ro = new ResizeObserver(([entry]) => {
      const next = Math.min(
        MAX_SCALE,
        Math.max(MIN_SCALE, entry.contentRect.width / REF_COLUMN_WIDTH),
      );
      setScale(next);
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const truckSize = Math.round(TRUCK_BASE * scale);
  const sparkleSize = Math.round(SPARKLE_BASE * scale);
  const maxRadius = Math.max(...PILLS.map((p) => p.radius)) * scale;
  const arcSize = Math.round((maxRadius * 2) + ARC_PADDING);

  return (
    <div
      ref={containerRef}
      className="brand-scene relative h-full w-full overflow-hidden"
    >
      {/* Pastel mesh + dot grid moved to LoginExperience so both columns
          share one seamless atmosphere — no more column-edge colour seam. */}

      {/* CONSTELLATION BACKDROP — absolute layer centred on the column.
          Pills + arcs + truck mark sit behind the text content. lg+ only. */}
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0 z-0 hidden lg:block"
      >
        <ConstellationArcs scale={scale} size={arcSize} />

        {PILLS.map((p) => {
          const rad = (p.angle * Math.PI) / 180;
          const r = p.radius * scale;
          const x = Math.cos(rad) * r;
          const y = Math.sin(rad) * r;
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
                initial={{ opacity: 0, scale: 0.85, y: 8 }}
                animate={{ opacity: 1, scale: 1, y: 0 }}
                transition={{ duration: 0.7, delay: p.delay, ease }}
              >
                <FeaturePill tone={p.tone} icon={p.icon} label={p.label} />
              </motion.div>
            </div>
          );
        })}

        {/* Centre — Truck mark. Same rainbow-conic squircle as the landing
            hero so the brand reads continuously across routes. */}
        <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2">
          <motion.div
            initial={{ opacity: 0, scale: 0.72, rotate: -18 }}
            animate={{ opacity: 1, scale: 1, rotate: -8 }}
            transition={{ duration: 0.95, delay: 0.55, ease }}
            className="relative"
          >
            <motion.div
              animate={{ y: [0, -6, 0] }}
              transition={{ duration: 6, ease: "easeInOut", repeat: Infinity }}
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

            {/* Slow revolving sparkle — single warm dot orbiting the mark. */}
            <motion.span
              aria-hidden
              animate={{ rotate: 360 }}
              transition={{ duration: 14, ease: "linear", repeat: Infinity }}
              className="absolute inset-0 grid place-items-center"
            >
              <span
                className="block rounded-full"
                style={{
                  width: sparkleSize,
                  height: sparkleSize,
                  background:
                    "radial-gradient(circle at top, var(--color-amber) 0%, transparent 6%)",
                }}
              />
            </motion.span>
          </motion.div>
        </div>
      </div>

      {/* TEXT + FOOTNOTE — sit on top of the backdrop. */}
      <div className="relative z-10 flex h-full flex-col justify-between px-10 py-12 xl:px-14">
        <div className="max-w-[560px]">
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.55, delay: 0.2, ease }}
            className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/65 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] backdrop-blur dark:border-white/[0.06] dark:bg-white/[0.05]"
          >
            <span className="relative flex h-1.5 w-1.5">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-[var(--color-success)] opacity-75" />
              <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-[var(--color-success)]" />
            </span>
            Welcome to the operations deck
          </motion.div>

          <h1 className="mt-6 font-display text-[2.75rem] leading-[1.02] tracking-[-0.035em] font-semibold text-[var(--color-ink-900)] md:text-[3.5rem] xl:text-[4rem]">
            {WORDS.map((w, i) => (
              <motion.span
                key={w}
                initial={{ opacity: 0, y: 18, filter: "blur(8px)" }}
                animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
                transition={{ duration: 0.75, delay: 0.3 + i * 0.13, ease }}
                className="block"
              >
                {w}
              </motion.span>
            ))}
          </h1>

          <motion.p
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.65, delay: 0.85, ease }}
            className="mt-6 max-w-[440px] text-[15.5px] leading-relaxed text-[var(--color-ink-500)] md:text-[16.5px]"
          >
            Live fleet activity, dispatch funnels, driver comms — one calm
            console for the long haul. Sign in to take the wheel.
          </motion.p>
        </div>

        {/* Footnote — mini trust strip */}
        <motion.p
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ duration: 0.6, delay: 1.4 }}
          className="mt-10 inline-flex items-center gap-3 text-[11.5px] uppercase tracking-[0.22em] text-[var(--color-ink-400)]"
        >
          <span
            className="inline-block h-px w-8"
            style={{ background: "currentColor", opacity: 0.55 }}
          />
          Trusted across 14 countries · 1,200+ carriers
        </motion.p>
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* FeaturePill — soft pastel chip with category-coded color. Uses existing    */
/* pastel tokens so light + dark themes both look right.                       */
/* -------------------------------------------------------------------------- */

function FeaturePill({
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
    <motion.span
      animate={{ y: [0, -4, 0] }}
      transition={{ duration: 7, ease: "easeInOut", repeat: Infinity }}
      className="glass glass-edge inline-flex items-center gap-2.5 rounded-full px-4 py-2.5 backdrop-blur"
      style={{ background: bg, borderColor: "rgba(255,255,255,0.5)" }}
    >
      <span
        className="grid h-7 w-7 place-items-center rounded-full"
        style={{ background: "rgba(255,255,255,0.65)", color: ink }}
      >
        <Icon className="h-3.5 w-3.5" strokeWidth={2.2} />
      </span>
      <span
        className="text-[12.5px] font-semibold tracking-tight"
        style={{ color: ink }}
      >
        {label}
      </span>
    </motion.span>
  );
}

/* -------------------------------------------------------------------------- */
/* ConstellationArcs — three faint dashed arcs from each pill toward the      */
/* centre, like freight routes on an atlas. The SVG renders at exactly the    */
/* same px size as its viewBox so arc end-points land precisely on the pill   */
/* anchors at every scale.                                                     */
/* -------------------------------------------------------------------------- */

function ConstellationArcs({ scale, size }: { scale: number; size: number }) {
  const half = size / 2;
  return (
    <svg
      aria-hidden
      viewBox={`${-half} ${-half} ${size} ${size}`}
      className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 text-[var(--color-ink-400)]"
      style={{ width: size, height: size }}
      preserveAspectRatio="xMidYMid meet"
    >
      <defs>
        <linearGradient id="arcFade" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="currentColor" stopOpacity="0.6" />
          <stop offset="100%" stopColor="currentColor" stopOpacity="0" />
        </linearGradient>
      </defs>
      {PILLS.map((p) => {
        const rad = (p.angle * Math.PI) / 180;
        const r = p.radius * scale;
        const x = Math.cos(rad) * r;
        const y = Math.sin(rad) * r;
        // Bow the curve outward via a perpendicular control point.
        const cx = (x / 2) + Math.cos(rad + Math.PI / 2) * 26;
        const cy = (y / 2) + Math.sin(rad + Math.PI / 2) * 26;
        return (
          <motion.path
            key={p.label}
            d={`M 0 0 Q ${cx} ${cy} ${x} ${y}`}
            fill="none"
            stroke="url(#arcFade)"
            strokeWidth="1.2"
            strokeDasharray="3 6"
            initial={{ pathLength: 0, opacity: 0 }}
            animate={{ pathLength: 1, opacity: 0.55 }}
            transition={{ duration: 1.4, delay: p.delay + 0.2, ease }}
          />
        );
      })}
    </svg>
  );
}
