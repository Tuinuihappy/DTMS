"use client";

import { ChevronDown, TrendingUp, Truck } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { peakDayIndex, weekActivity } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

const periods = ["Day", "Week", "Month"] as const;

export function LiveActivityCard() {
  const [period, setPeriod] = useState<(typeof periods)[number]>("Week");
  const [active, setActive] = useState(peakDayIndex);

  const max = Math.max(...weekActivity.map((d) => d.shipments));
  const peak = weekActivity[active];

  return (
    <GlassCard
      variant="strong"
      className="p-7 lg:p-8 col-span-12 lg:col-span-8"
      initial={{ opacity: 0, y: 24 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.65, ease: [0.22, 1, 0.36, 1], delay: 0.15 }}
    >
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex items-start gap-3.5">
          <span className="grid h-11 w-11 place-items-center rounded-[14px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[#c8d4ff] text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_10px_-4px_rgba(15,23,42,0.12)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_4px_10px_-4px_rgba(0,0,0,0.5)]">
            <Truck className="h-5 w-5" strokeWidth={2} />
          </span>
          <div>
            <h2 className="font-display text-[1.65rem] md:text-[1.85rem] font-semibold leading-tight tracking-tight text-[var(--color-ink-900)]">
              Live Fleet Activity
            </h2>
            <p className="mt-1 max-w-md text-[13.5px] text-[var(--color-ink-500)]">
              Dispatched shipments and gross freight value across the active week
            </p>
          </div>
        </div>

        {/* Period selector */}
        <div className="flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 p-1 dark:bg-white/[0.05]">
          {periods.map((p) => (
            <button
              key={p}
              onClick={() => setPeriod(p)}
              className={cn(
                "rounded-full px-3.5 py-1.5 text-[12px] font-medium transition-colors cursor-pointer",
                period === p
                  ? "bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]"
                  : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-800)]",
              )}
            >
              {p}
            </button>
          ))}
          <button className="ml-0.5 grid h-7 w-7 place-items-center rounded-full text-[var(--color-ink-500)] hover:bg-[var(--color-ink-50)] cursor-pointer">
            <ChevronDown className="h-3.5 w-3.5" strokeWidth={2.4} />
          </button>
        </div>
      </div>

      {/* Lollipop chart */}
      <div className="mt-10 grid grid-cols-7 gap-2 md:gap-4 h-[260px] relative">
        {/* Soft baseline */}
        <div className="absolute inset-x-0 bottom-10 h-px bg-[var(--color-ink-100)]" />

        {weekActivity.map((d, i) => {
          const ratio = d.shipments / max;
          const stemHeight = 30 + ratio * 170;
          const isActive = i === active;
          return (
            <button
              key={i}
              onClick={() => setActive(i)}
              className="relative group flex flex-col items-center justify-end cursor-pointer"
            >
              {/* Stem */}
              <motion.div
                initial={{ height: 0, opacity: 0 }}
                animate={{ height: stemHeight, opacity: 1 }}
                transition={{
                  duration: 0.9,
                  delay: 0.4 + i * 0.06,
                  ease: [0.22, 1, 0.36, 1],
                }}
                className="relative mb-10 w-px"
                style={{
                  background: isActive
                    ? "linear-gradient(180deg, var(--color-brand-900), rgba(14,21,48,0.05))"
                    : "linear-gradient(180deg, var(--color-ink-200), rgba(124,137,166,0.05))",
                }}
              >
                {/* Dot at top */}
                <motion.div
                  initial={{ scale: 0, opacity: 0 }}
                  animate={{ scale: 1, opacity: 1 }}
                  transition={{
                    duration: 0.5,
                    delay: 0.95 + i * 0.06,
                    ease: [0.34, 1.56, 0.64, 1],
                  }}
                  className={cn(
                    "absolute left-1/2 -top-2 -translate-x-1/2 h-4 w-4 rounded-full transition-all duration-300",
                    isActive
                      ? "bg-[var(--color-brand-900)] ring-4 ring-[var(--color-brand-200)]/60"
                      : "bg-white border-2 border-[var(--color-ink-300)] group-hover:border-[var(--color-brand-500)]",
                  )}
                />
              </motion.div>

              {/* Tooltip on active */}
              <AnimatePresence>
                {isActive && (
                  <motion.div
                    layoutId="activity-tooltip"
                    initial={{ opacity: 0, y: 6, scale: 0.94 }}
                    animate={{ opacity: 1, y: 0, scale: 1 }}
                    exit={{ opacity: 0, y: 6, scale: 0.94 }}
                    transition={{ duration: 0.25, ease: [0.22, 1, 0.36, 1] }}
                    className="absolute z-10 rounded-2xl bg-[var(--color-brand-900)] text-white px-3 py-2 shadow-[0_10px_30px_-10px_rgba(14,21,48,0.6)]"
                    style={{ bottom: stemHeight + 22 }}
                  >
                    <div className="font-mono text-[15px] font-semibold tracking-tight">
                      ${peak.value.toLocaleString()}
                    </div>
                    <div className="text-[10px] uppercase tracking-[0.12em] text-white/60">
                      {peak.shipments} shipments
                    </div>
                    <span
                      className="absolute left-1/2 -translate-x-1/2 -bottom-1 h-2 w-2 rotate-45 bg-[var(--color-brand-900)]"
                      aria-hidden
                    />
                  </motion.div>
                )}
              </AnimatePresence>

              {/* Day label */}
              <div
                className={cn(
                  "absolute bottom-0 grid h-8 w-8 place-items-center rounded-full text-[11px] font-semibold transition-colors",
                  isActive
                    ? "bg-[var(--color-brand-900)] text-white"
                    : "bg-[var(--color-ink-50)] text-[var(--color-ink-500)] group-hover:bg-[var(--color-ink-100)]",
                )}
              >
                {d.day}
              </div>
            </button>
          );
        })}
      </div>

      {/* Footer: delta */}
      <div className="mt-8 flex flex-wrap items-end justify-between gap-6 border-t border-[var(--color-ink-100)] pt-7">
        <div>
          <div className="flex items-baseline gap-2">
            <span className="font-display text-[3rem] font-semibold leading-none tracking-[-0.04em] text-[var(--color-ink-900)]">
              +<NumberTicker value={18} suffix="%" />
            </span>
            <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-2 py-1 text-[11px] font-semibold text-[var(--color-success)]">
              <TrendingUp className="h-3 w-3" strokeWidth={2.6} />
              wk over wk
            </span>
          </div>
          <p className="mt-2 max-w-xs text-[13px] leading-relaxed text-[var(--color-ink-500)]">
            This week&apos;s dispatch volume is higher than last week&apos;s, lifted by the
            Eastern Seaboard corridor reopening.
          </p>
        </div>

        <div className="grid grid-cols-3 gap-6 sm:gap-10">
          <Stat label="Gross freight" value="$142.8k" />
          <Stat label="On-time" value="96.4%" highlight />
          <Stat label="Avg margin" value="22.1%" />
        </div>
      </div>
    </GlassCard>
  );
}

function Stat({
  label,
  value,
  highlight,
}: {
  label: string;
  value: string;
  highlight?: boolean;
}) {
  return (
    <div>
      <div className="text-[10.5px] uppercase tracking-[0.14em] font-medium text-[var(--color-ink-400)]">
        {label}
      </div>
      <div
        className={cn(
          "font-mono text-[1.05rem] font-semibold mt-0.5",
          highlight ? "text-[var(--color-success)]" : "text-[var(--color-ink-800)]",
        )}
      >
        {value}
      </div>
    </div>
  );
}
