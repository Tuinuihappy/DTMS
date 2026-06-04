"use client";

import { Activity, CheckCircle2, ClipboardList, Weight } from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";
import { NumberTicker } from "@/components/primitives/number-ticker";
import { StatusPulse } from "@/components/primitives/status-pulse";
import type { OrderStats } from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";

const TILE_TONES = {
  brand:
    "from-[var(--color-pastel-sky)] to-[#c7d4ff] text-[var(--color-brand-900)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)]",
  amber:
    "from-[var(--color-amber-soft)] to-[#fcd398] text-[#8a4a07] dark:to-[#6a4a1c] dark:text-[var(--color-amber)]",
  success:
    "from-[var(--color-success-soft)] to-[#b6e8cf] text-[var(--color-success)] dark:to-[#1f5a40] dark:text-[var(--color-success)]",
  ink: "from-[var(--color-ink-100)] to-[#cfd6e6] text-[var(--color-ink-800)] dark:to-[#3a4870] dark:text-[var(--color-ink-700)]",
} as const;

export function OrdersKpiStrip({
  stats,
  loading,
}: {
  stats: OrderStats | null;
  loading?: boolean;
}) {
  const cards: Array<{
    icon: React.ReactNode;
    label: string;
    value: number;
    suffix?: string;
    decimals?: number;
    tone: keyof typeof TILE_TONES;
    live?: boolean;
  }> = [
    {
      icon: <ClipboardList className="h-4 w-4" strokeWidth={2.2} />,
      label: "Total orders",
      value: stats?.total ?? 0,
      tone: "brand",
    },
    {
      icon: <Activity className="h-4 w-4" strokeWidth={2.2} />,
      label: "In flight",
      value: stats?.active ?? 0,
      tone: "amber",
      live: (stats?.active ?? 0) > 0,
    },
    {
      icon: <CheckCircle2 className="h-4 w-4" strokeWidth={2.2} />,
      label: "Completed",
      value: stats?.completed ?? 0,
      tone: "success",
    },
    {
      icon: <Weight className="h-4 w-4" strokeWidth={2.2} />,
      label: "Total weight",
      value: stats?.totalWeightKg ?? 0,
      suffix: " kg",
      decimals: 0,
      tone: "ink",
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-4">
      {cards.map((c, i) => (
        <GlassCard
          key={i}
          variant="default"
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.55, delay: 0.08 + i * 0.05, ease: [0.22, 1, 0.36, 1] }}
          className="p-4 sm:p-5"
        >
          <div className="flex items-start justify-between gap-3">
            <span
              className={cn(
                "grid h-9 w-9 place-items-center rounded-[12px] bg-gradient-to-br shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]",
                TILE_TONES[c.tone],
              )}
            >
              {c.icon}
            </span>
            {c.live && (
              <span className="inline-flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-amber)]">
                <StatusPulse tone="amber" />
                Live
              </span>
            )}
          </div>
          <div className="mt-4">
            <div className="text-[10.5px] sm:text-[11px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-400)]">
              {c.label}
            </div>
            <div className="mt-1 flex items-baseline gap-1 font-mono text-[1.55rem] sm:text-[1.65rem] font-semibold leading-none text-[var(--color-ink-900)]">
              {loading ? (
                <motion.span
                  initial={{ opacity: 0.3 }}
                  animate={{ opacity: [0.3, 0.7, 0.3] }}
                  transition={{ duration: 1.4, repeat: Infinity }}
                  className="inline-block h-7 w-12 rounded-md bg-[var(--color-ink-100)] dark:bg-white/10"
                />
              ) : (
                <NumberTicker value={c.value} decimals={c.decimals ?? 0} />
              )}
              {c.suffix && !loading && (
                <span className="text-[var(--color-ink-400)] text-sm font-medium">{c.suffix}</span>
              )}
            </div>
          </div>
        </GlassCard>
      ))}
    </div>
  );
}
