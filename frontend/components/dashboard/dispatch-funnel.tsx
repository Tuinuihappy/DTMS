"use client";

import { Calendar, Filter } from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { dispatchFunnel } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

const toneClass: Record<"ink" | "amber" | "coral", string> = {
  ink: "bg-[var(--color-ink-800)]",
  amber: "bg-[var(--color-amber)]",
  coral: "bg-[var(--color-coral)]",
};
const toneText: Record<"ink" | "amber" | "coral", string> = {
  ink: "text-[var(--color-ink-800)]",
  amber: "text-[var(--color-amber)]",
  coral: "text-[var(--color-coral)]",
};

export function DispatchFunnel() {
  const max = Math.max(...dispatchFunnel.map((f) => f.count));

  return (
    <GlassCard
      variant="default"
      className="col-span-12 lg:col-span-5 p-6"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.7, ease: [0.22, 1, 0.36, 1] }}
    >
      <SectionLabel
        title="Dispatch funnel"
        subtitle="Conversion across pipeline stages — last 7 days"
        action={
          <div className="flex items-center gap-1.5">
            <button className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]">
              <Filter className="h-3 w-3" strokeWidth={2.4} />
              All routes
            </button>
            <button className="inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]">
              <Calendar className="h-3 w-3" strokeWidth={2.4} />
              May 24 — 31
            </button>
          </div>
        }
      />

      {/* Bar chart */}
      <div className="mt-8 grid grid-cols-5 gap-4 md:gap-6 h-[200px] items-end">
        {dispatchFunnel.map((f, i) => {
          const ratio = f.count / max;
          const ticks = Math.round(ratio * 26);
          return (
            <div key={f.label} className="flex flex-col items-center justify-end gap-3">
              {/* Vertical tick stack */}
              <div className="relative w-full h-full flex flex-col justify-end items-center">
                <div className="flex flex-col items-center gap-[3px] w-full">
                  {Array.from({ length: ticks }).map((_, t) => (
                    <motion.span
                      key={t}
                      initial={{ scaleX: 0, opacity: 0 }}
                      animate={{ scaleX: 1, opacity: 1 }}
                      transition={{
                        duration: 0.3,
                        delay: 0.9 + i * 0.08 + t * 0.012,
                        ease: [0.22, 1, 0.36, 1],
                      }}
                      className={cn(
                        "h-[3px] w-full rounded-full origin-bottom",
                        toneClass[f.color],
                      )}
                      style={{
                        opacity: 0.25 + (t / ticks) * 0.75,
                      }}
                    />
                  ))}
                </div>
              </div>

              {/* Number + label */}
              <div className="text-center pt-2 border-t border-[var(--color-ink-100)] w-full">
                <div className={cn("font-mono text-[1.15rem] font-semibold leading-none", toneText[f.color])}>
                  {f.count}
                </div>
                <div className="mt-1.5 text-[10.5px] uppercase tracking-[0.1em] font-medium text-[var(--color-ink-500)]">
                  {f.label}
                </div>
              </div>
            </div>
          );
        })}
      </div>

      <div className="mt-6 inset-divider" />
      <div className="mt-4 flex items-center justify-between text-[11.5px] text-[var(--color-ink-500)]">
        <span>
          Conversion:{" "}
          <span className="font-mono font-semibold text-[var(--color-ink-900)]">50.6%</span>
        </span>
        <span>
          Median time-to-dispatch:{" "}
          <span className="font-mono font-semibold text-[var(--color-ink-900)]">2h 14m</span>
        </span>
      </div>
    </GlassCard>
  );
}
