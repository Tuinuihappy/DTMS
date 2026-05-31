"use client";

import { Activity, ArrowUpRight, Gauge, MapPin, Truck } from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

type Kpi = {
  icon: React.ReactNode;
  label: string;
  value: number;
  unit?: string;
  decimals?: number;
  delta?: { value: string; positive: boolean };
  sparkline?: number[];
  pulse?: boolean;
  tone: "brand" | "amber" | "success" | "ink";
};

const kpis: Kpi[] = [
  {
    icon: <Truck className="h-4 w-4" strokeWidth={2.2} />,
    label: "Active fleet",
    value: 142,
    delta: { value: "+6", positive: true },
    sparkline: [3, 5, 4, 6, 5, 7, 8],
    tone: "brand",
  },
  {
    icon: <Gauge className="h-4 w-4" strokeWidth={2.2} />,
    label: "On-time rate",
    value: 96.4,
    unit: "%",
    decimals: 1,
    delta: { value: "+1.2", positive: true },
    sparkline: [4, 5, 5, 6, 7, 6, 8],
    tone: "success",
  },
  {
    icon: <Activity className="h-4 w-4" strokeWidth={2.2} />,
    label: "In transit",
    value: 47,
    pulse: true,
    delta: { value: "live", positive: true },
    tone: "amber",
  },
  {
    icon: <MapPin className="h-4 w-4" strokeWidth={2.2} />,
    label: "Distance today",
    value: 8420,
    unit: " km",
    sparkline: [2, 4, 3, 5, 6, 5, 7],
    tone: "ink",
  },
];

const toneRing: Record<Kpi["tone"], string> = {
  brand:
    "from-[var(--color-pastel-sky)] to-[#c7d4ff] text-[var(--color-brand-900)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)]",
  amber:
    "from-[var(--color-amber-soft)] to-[#fcd398] text-[#8a4a07] dark:to-[#6a4a1c] dark:text-[var(--color-amber)]",
  success:
    "from-[var(--color-success-soft)] to-[#b6e8cf] text-[var(--color-success)] dark:to-[#1f5a40] dark:text-[var(--color-success)]",
  ink: "from-[var(--color-ink-100)] to-[#cfd6e6] text-[var(--color-ink-800)] dark:to-[#3a4870] dark:text-[var(--color-ink-700)]",
};

export function KpiRail() {
  return (
    <div className="col-span-12 lg:col-span-4 grid grid-cols-2 lg:grid-cols-1 gap-4">
      {kpis.map((k, i) => (
        <GlassCard
          key={i}
          variant="default"
          interactive
          initial={{ opacity: 0, y: 18 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55, delay: 0.2 + i * 0.06, ease: [0.22, 1, 0.36, 1] }}
          className="p-5"
        >
          <div className="flex items-start justify-between gap-3">
            <span
              className={cn(
                "grid h-9 w-9 place-items-center rounded-[12px] bg-gradient-to-br shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]",
                toneRing[k.tone],
              )}
            >
              {k.icon}
            </span>
            {k.pulse ? (
              <span className="inline-flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-amber)]">
                <StatusPulse tone="amber" />
                Live
              </span>
            ) : k.delta ? (
              <span
                className={cn(
                  "inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-[10.5px] font-semibold",
                  k.delta.positive
                    ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
                    : "bg-[#fde0db] text-[var(--color-coral)]",
                )}
              >
                <ArrowUpRight className="h-2.5 w-2.5" strokeWidth={2.6} />
                {k.delta.value}
              </span>
            ) : null}
          </div>

          <div className="mt-4 flex items-end justify-between gap-3">
            <div className="min-w-0">
              <div className="text-[11px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-400)]">
                {k.label}
              </div>
              <div className="font-mono text-[1.65rem] font-semibold leading-none mt-1 text-[var(--color-ink-900)]">
                <NumberTicker value={k.value} decimals={k.decimals ?? 0} />
                {k.unit && (
                  <span className="text-[var(--color-ink-400)] text-base font-medium ml-0.5">
                    {k.unit}
                  </span>
                )}
              </div>
            </div>
            {k.sparkline && <Sparkline values={k.sparkline} tone={k.tone} delay={0.4 + i * 0.06} />}
          </div>
        </GlassCard>
      ))}
    </div>
  );
}

function Sparkline({
  values,
  tone,
  delay = 0,
}: {
  values: number[];
  tone: Kpi["tone"];
  delay?: number;
}) {
  const max = Math.max(...values);
  const stroke =
    tone === "brand"
      ? "var(--color-brand-500)"
      : tone === "success"
        ? "var(--color-success)"
        : tone === "amber"
          ? "var(--color-amber)"
          : "var(--color-ink-700)";
  const w = 64;
  const h = 28;
  const step = w / (values.length - 1);
  const points = values
    .map((v, i) => `${i * step},${h - (v / max) * (h - 4) - 2}`)
    .join(" ");
  const area = `M0,${h} L${points.split(" ").join(" L")} L${w},${h} Z`;
  return (
    <motion.svg
      width={w}
      height={h}
      viewBox={`0 0 ${w} ${h}`}
      className="overflow-visible"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.6, delay }}
    >
      <defs>
        <linearGradient id={`spark-${tone}`} x1="0" x2="0" y1="0" y2="1">
          <stop offset="0%" stopColor={stroke} stopOpacity={0.32} />
          <stop offset="100%" stopColor={stroke} stopOpacity={0} />
        </linearGradient>
      </defs>
      <motion.path
        d={area}
        fill={`url(#spark-${tone})`}
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
        transition={{ duration: 0.9, delay, ease: [0.22, 1, 0.36, 1] }}
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
