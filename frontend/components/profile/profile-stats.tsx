"use client";

import { AlertCircle, ArrowUpRight, Clock4, Gauge, PackageCheck, Truck } from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

type Stat = {
  icon: React.ReactNode;
  label: string;
  value: number;
  unit?: string;
  decimals?: number;
  delta?: { value: string; positive: boolean };
  tone: "brand" | "amber" | "success" | "coral" | "ink";
  live?: boolean;
  spark: number[];
};

const stats: Stat[] = [
  {
    icon: <PackageCheck className="h-4 w-4" strokeWidth={2.2} />,
    label: "Deliveries today",
    value: 38,
    delta: { value: "+4", positive: true },
    tone: "brand",
    spark: [2, 4, 3, 5, 6, 4, 7, 8],
  },
  {
    icon: <Gauge className="h-4 w-4" strokeWidth={2.2} />,
    label: "On-time rate",
    value: 97.8,
    unit: "%",
    decimals: 1,
    delta: { value: "+1.4", positive: true },
    tone: "success",
    spark: [4, 5, 5, 6, 7, 7, 8, 8],
  },
  {
    icon: <Truck className="h-4 w-4" strokeWidth={2.2} />,
    label: "Active trucks",
    value: 12,
    live: true,
    tone: "amber",
    spark: [3, 4, 5, 4, 6, 7, 6, 7],
  },
  {
    icon: <AlertCircle className="h-4 w-4" strokeWidth={2.2} />,
    label: "Open issues",
    value: 2,
    delta: { value: "-1", positive: true },
    tone: "coral",
    spark: [6, 5, 4, 5, 3, 4, 3, 2],
  },
  {
    icon: <Clock4 className="h-4 w-4" strokeWidth={2.2} />,
    label: "Hours on shift",
    value: 7.2,
    unit: "h",
    decimals: 1,
    delta: { value: "0:48 left", positive: true },
    tone: "ink",
    spark: [1, 2, 3, 4, 5, 6, 7, 7],
  },
];

const toneRing: Record<Stat["tone"], string> = {
  brand:
    "from-[var(--color-pastel-sky)] to-[#c7d4ff] text-[var(--color-brand-900)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)]",
  amber:
    "from-[var(--color-amber-soft)] to-[#fcd398] text-[#8a4a07] dark:to-[#6a4a1c] dark:text-[var(--color-amber)]",
  success:
    "from-[var(--color-success-soft)] to-[#b6e8cf] text-[var(--color-success)] dark:to-[#1f5a40] dark:text-[var(--color-success)]",
  coral:
    "from-[#fde0db] to-[#ffc8bf] text-[var(--color-coral)] dark:from-[#3a1d18] dark:to-[#5a2820] dark:text-[var(--color-coral)]",
  ink: "from-[var(--color-ink-100)] to-[#cfd6e6] text-[var(--color-ink-800)] dark:to-[#3a4870] dark:text-[var(--color-ink-700)]",
};

export function ProfileStats() {
  return (
    <section className="mt-6 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
      {stats.map((s, i) => (
        <GlassCard
          key={s.label}
          variant="default"
          interactive
          initial={{ opacity: 0, y: 18 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55, delay: 0.65 + i * 0.07, ease: EASE }}
          className={cn(
            "p-5",
            // First card on small grid (cols=2) gets a span to anchor as
            // featured stat the way the reference template does. On
            // larger grids every card is equal width.
            i === 0 && "col-span-2 sm:col-span-1",
          )}
        >
          <div className="flex items-start justify-between gap-3">
            <span
              className={cn(
                "grid h-9 w-9 place-items-center rounded-[12px] bg-gradient-to-br shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]",
                toneRing[s.tone],
              )}
            >
              {s.icon}
            </span>
            {s.live ? (
              <span className="inline-flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-amber)]">
                <StatusPulse tone="amber" />
                Live
              </span>
            ) : s.delta ? (
              <span
                className={cn(
                  "inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-[10.5px] font-semibold",
                  s.delta.positive
                    ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
                    : "bg-[#fde0db] text-[var(--color-coral)]",
                )}
              >
                <ArrowUpRight className="h-2.5 w-2.5" strokeWidth={2.6} />
                {s.delta.value}
              </span>
            ) : null}
          </div>

          <div className="mt-4 flex items-end justify-between gap-3">
            <div className="min-w-0">
              <div className="text-[11px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-400)]">
                {s.label}
              </div>
              <div className="font-mono text-[1.65rem] font-semibold leading-none mt-1 text-[var(--color-ink-900)]">
                <NumberTicker value={s.value} decimals={s.decimals ?? 0} />
                {s.unit && (
                  <span className="text-[var(--color-ink-400)] text-base font-medium ml-0.5">
                    {s.unit}
                  </span>
                )}
              </div>
            </div>
            <Sparkline values={s.spark} tone={s.tone} delay={0.85 + i * 0.07} />
          </div>
        </GlassCard>
      ))}
    </section>
  );
}

function Sparkline({
  values,
  tone,
  delay,
}: {
  values: number[];
  tone: Stat["tone"];
  delay: number;
}) {
  const max = Math.max(...values);
  const stroke =
    tone === "brand"
      ? "var(--color-brand-500)"
      : tone === "success"
        ? "var(--color-success)"
        : tone === "amber"
          ? "var(--color-amber)"
          : tone === "coral"
            ? "var(--color-coral)"
            : "var(--color-ink-700)";
  const w = 64;
  const h = 28;
  const step = w / (values.length - 1);
  const points = values
    .map((v, i) => `${i * step},${h - (v / max) * (h - 4) - 2}`)
    .join(" ");
  const area = `M0,${h} L${points.split(" ").join(" L")} L${w},${h} Z`;
  const id = `sp-${tone}-${delay}`;
  return (
    <motion.svg
      width={w}
      height={h}
      viewBox={`0 0 ${w} ${h}`}
      className="overflow-visible shrink-0"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.6, delay }}
    >
      <defs>
        <linearGradient id={id} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={stroke} stopOpacity={0.32} />
          <stop offset="100%" stopColor={stroke} stopOpacity={0} />
        </linearGradient>
      </defs>
      <motion.path
        d={area}
        fill={`url(#${id})`}
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        transition={{ duration: 0.7, delay: delay + 0.2 }}
      />
      <motion.polyline
        points={points}
        fill="none"
        stroke={stroke}
        strokeWidth={1.6}
        strokeLinecap="round"
        strokeLinejoin="round"
        initial={{ pathLength: 0 }}
        animate={{ pathLength: 1 }}
        transition={{ duration: 0.9, delay, ease: EASE }}
      />
      <circle
        cx={w}
        cy={h - (values[values.length - 1] / max) * (h - 4) - 2}
        r={2.5}
        fill={stroke}
      />
    </motion.svg>
  );
}
