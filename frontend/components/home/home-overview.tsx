"use client";

import { Activity, Gauge, MapPin, Truck } from "lucide-react";
import { motion } from "motion/react";
import type { ReactNode } from "react";

import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";
import { HeroLiveMap } from "./hero-live-map";

const ease = [0.22, 1, 0.36, 1] as const;

type Tone = "brand" | "success" | "amber" | "ink";

type Tile = {
  label: string;
  icon: ReactNode;
  value: string;
  unit?: string;
  trend?: { dir: "up" | "down"; delta: string };
  live?: boolean;
  spark?: number[];
  tone: Tone;
};

/* -------------------------------------------------------------------------- */
/* Mock numbers — the operator KPIs shown to the right of the live map. These */
/* are placeholders until the real telemetry pipeline exposes them.           */
/* -------------------------------------------------------------------------- */
const TILES: Tile[] = [
  {
    label: "Active fleet",
    icon: <Truck className="h-4 w-4" strokeWidth={2.2} />,
    value: "142",
    trend: { dir: "up", delta: "+6" },
    spark: [120, 124, 127, 125, 130, 134, 138, 142],
    tone: "brand",
  },
  {
    label: "On-time rate",
    icon: <Gauge className="h-4 w-4" strokeWidth={2.2} />,
    value: "96.4",
    unit: "%",
    trend: { dir: "up", delta: "+1.2" },
    spark: [94.2, 94.6, 95.0, 94.7, 95.4, 95.8, 96.1, 96.4],
    tone: "success",
  },
  {
    label: "In transit",
    icon: <Activity className="h-4 w-4" strokeWidth={2.2} />,
    value: "47",
    live: true,
    tone: "amber",
  },
  {
    label: "Distance today",
    icon: <MapPin className="h-4 w-4" strokeWidth={2.2} />,
    value: "8,420",
    unit: "km",
    spark: [7400, 7600, 7820, 7900, 8050, 8180, 8320, 8420],
    tone: "ink",
  },
];

const TONE_BG: Record<Tone, string> = {
  brand: "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]",
  success: "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
  amber: "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
  ink: "bg-[var(--color-ink-100)] text-[var(--color-ink-800)] dark:bg-white/[0.06] dark:text-[var(--color-ink-700)]",
};

const TONE_LINE: Record<Tone, string> = {
  brand: "var(--color-brand-500)",
  success: "var(--color-success)",
  amber: "var(--color-amber)",
  ink: "var(--color-ink-600)",
};

/* -------------------------------------------------------------------------- */
/* HomeOverview — the /home dashboard layout the operator lands on.           */
/* Left: live facility cartography (real data from RIOT3).                    */
/* Right: a 4-tile mock KPI column — Active fleet, On-time rate, In transit,  */
/* Distance today. Wire real telemetry in once the endpoints exist.           */
/* -------------------------------------------------------------------------- */
export function HomeOverview() {
  return (
    <section className="relative">
      <div className="grid grid-cols-1 gap-5 lg:grid-cols-12 lg:gap-6 lg:items-stretch">
        {/* Map — fills the row height on lg+ */}
        <div className="lg:col-span-8 lg:h-[680px]">
          <HeroLiveMap />
        </div>

        {/* KPI column — 4 tiles stacked, total height matches the map */}
        <div className="lg:col-span-4 grid grid-cols-1 gap-4 lg:h-[680px] lg:grid-rows-4">
          {TILES.map((t, i) => (
            <KpiCard key={t.label} tile={t} index={i} />
          ))}
        </div>
      </div>
    </section>
  );
}

/* -------------------------------------------------------------------------- */
/* KpiCard — single tile. Icon chip top-left, trend/live badge top-right,    */
/* label, large value, sparkline along the bottom.                            */
/* -------------------------------------------------------------------------- */
function KpiCard({ tile, index }: { tile: Tile; index: number }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, delay: 0.1 + index * 0.06, ease }}
      className="relative flex flex-col justify-between rounded-[var(--radius-xl)] glass-strong glass-edge p-4 sm:p-5 overflow-hidden min-h-[140px]"
    >
      {/* Top row: icon chip + trend / live badge */}
      <div className="relative flex items-start justify-between">
        <span
          className={cn(
            "grid h-9 w-9 place-items-center rounded-[12px] shadow-[inset_0_1px_0_rgba(255,255,255,0.4)]",
            TONE_BG[tile.tone],
          )}
        >
          {tile.icon}
        </span>

        {tile.live && (
          <span className="inline-flex items-center gap-1.5 text-[10.5px] font-mono uppercase tracking-[0.14em] text-[var(--color-amber)]">
            <StatusPulse tone="amber" />
            <span className="font-semibold">live</span>
          </span>
        )}
        {tile.trend && (
          <span
            className={cn(
              "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-semibold tracking-tight",
              tile.trend.dir === "up"
                ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
                : "bg-[var(--color-amber-soft)] text-[var(--color-amber)]",
            )}
          >
            <TrendArrow dir={tile.trend.dir} />
            {tile.trend.delta}
          </span>
        )}
      </div>

      {/* Middle: label + value */}
      <div className="relative mt-3">
        <div className="text-[10px] font-semibold uppercase tracking-[0.16em] text-[var(--color-ink-400)]">
          {tile.label}
        </div>
        <div className="mt-1 flex items-baseline gap-1.5">
          <span className="font-display text-[2rem] font-semibold leading-none tracking-[-0.02em] text-[var(--color-ink-900)] sm:text-[2.25rem]">
            {tile.value}
          </span>
          {tile.unit && (
            <span className="text-[13px] font-medium text-[var(--color-ink-400)]">
              {tile.unit}
            </span>
          )}
        </div>
      </div>

      {/* Bottom: sparkline tucked into the bottom-right; soaks the empty space
          without crowding the number. Hidden when there's no series. */}
      {tile.spark && (
        <div className="pointer-events-none absolute bottom-3 right-3 h-9 w-24 sm:w-28">
          <Sparkline values={tile.spark} stroke={TONE_LINE[tile.tone]} />
        </div>
      )}
    </motion.div>
  );
}

/* -------------------------------------------------------------------------- */
/* Sparkline — tiny inline trend chart. Normalises the input series into the  */
/* viewBox, draws a smooth path, and tops it with a soft area fill.           */
/* -------------------------------------------------------------------------- */
function Sparkline({ values, stroke }: { values: number[]; stroke: string }) {
  if (values.length < 2) return null;
  const W = 100;
  const H = 36;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  const step = W / (values.length - 1);
  const points = values.map((v, i) => {
    const x = i * step;
    const y = H - ((v - min) / span) * (H - 4) - 2;
    return { x, y };
  });
  const linePath = points
    .map((p, i) => `${i === 0 ? "M" : "L"} ${p.x.toFixed(2)} ${p.y.toFixed(2)}`)
    .join(" ");
  const areaPath = `${linePath} L ${W} ${H} L 0 ${H} Z`;

  return (
    <svg
      viewBox={`0 0 ${W} ${H}`}
      preserveAspectRatio="none"
      className="h-full w-full"
      aria-hidden
    >
      <path d={areaPath} fill={stroke} fillOpacity={0.12} />
      <path d={linePath} fill="none" stroke={stroke} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={points[points.length - 1]!.x} cy={points[points.length - 1]!.y} r={2.2} fill={stroke} />
    </svg>
  );
}

function TrendArrow({ dir }: { dir: "up" | "down" }) {
  return (
    <svg width="10" height="10" viewBox="0 0 12 12" fill="none" aria-hidden>
      <path
        d={dir === "up" ? "M3 9L9 3M9 3H4M9 3V8" : "M3 3L9 9M9 9H4M9 9V4"}
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
