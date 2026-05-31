"use client";

import { ArrowRight, ChevronRight, Clock, MapPin } from "lucide-react";
import { motion } from "motion/react";
import { AvatarStack } from "@/components/primitives/avatar";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { priorityShipments, type PriorityShipment } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

const tierStyles: Record<PriorityShipment["tier"], { card: string; ink: string; chip: string; bar: string; route: string }> = {
  sky: {
    card: "pastel-sky",
    ink: "text-[var(--color-pastel-sky-ink)]",
    chip: "bg-white/70 text-[var(--color-pastel-sky-ink)] dark:bg-white/[0.08]",
    bar: "bg-[var(--color-pastel-sky-ink)]",
    route: "text-[var(--color-pastel-sky-ink)]",
  },
  lavender: {
    card: "pastel-lavender",
    ink: "text-[var(--color-pastel-lavender-ink)]",
    chip: "bg-white/70 text-[var(--color-pastel-lavender-ink)]",
    bar: "bg-[var(--color-pastel-lavender-ink)]",
    route: "text-[var(--color-pastel-lavender-ink)]",
  },
  peach: {
    card: "pastel-peach",
    ink: "text-[var(--color-pastel-peach-ink)]",
    chip: "bg-white/70 text-[var(--color-pastel-peach-ink)]",
    bar: "bg-[var(--color-pastel-peach-ink)]",
    route: "text-[var(--color-pastel-peach-ink)]",
  },
};

export function PriorityShipments() {
  return (
    <div className="col-span-12 mt-2">
      <SectionLabel
        title="Priority dispatches"
        subtitle="Three shipments need supervisor approval before their window closes today."
        action={
          <a
            href="#"
            className="inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3.5 py-1.5 text-[12px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
          >
            See all 14
            <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.4} />
          </a>
        }
        className="mb-5"
      />
      <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
        {priorityShipments.map((s, i) => {
          const t = tierStyles[s.tier];
          const pct = Math.round((s.progressKm / s.distanceKm) * 100);
          return (
            <GlassCard
              key={s.id}
              variant={t.card as "pastel-sky"}
              interactive
              initial={{ opacity: 0, y: 24 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{
                duration: 0.55,
                delay: 0.5 + i * 0.08,
                ease: [0.22, 1, 0.36, 1],
              }}
              className="p-6 min-h-[260px] flex flex-col"
            >
              {/* Top badge */}
              <div className="flex items-center justify-between gap-3">
                <span
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[10.5px] uppercase tracking-[0.12em] font-semibold",
                    t.chip,
                  )}
                >
                  <span className={cn("h-1.5 w-1.5 rounded-full", t.bar)} />
                  {s.badge}
                </span>
                <span className={cn("font-mono text-[11px] font-medium", t.ink)}>{s.id}</span>
              </div>

              {/* Route */}
              <h4
                className={cn(
                  "mt-5 font-display text-[1.7rem] font-semibold leading-[1.05] tracking-tight",
                  t.route,
                )}
              >
                {s.route}
              </h4>

              {/* Origin → Destination */}
              <div className={cn("mt-3 flex items-center gap-2 text-[12.5px] font-medium", t.ink)}>
                <MapPin className="h-3.5 w-3.5 opacity-60" strokeWidth={2.2} />
                <span>{s.origin}</span>
                <ArrowRight className="h-3 w-3 opacity-50" strokeWidth={2.4} />
                <span>{s.destination}</span>
              </div>

              <p className={cn("mt-1 text-[12.5px] opacity-70", t.ink)}>{s.cargo}</p>

              <div className="flex-1" />

              {/* Drivers + ETA */}
              <div className="mt-5 flex items-center justify-between">
                <AvatarStack people={s.drivers} size="sm" max={3} />
                <div className={cn("flex items-center gap-1 text-[12px] font-medium", t.ink)}>
                  <Clock className="h-3.5 w-3.5" strokeWidth={2.2} />
                  <span className="font-mono">{s.etaHours.toFixed(1)}h ETA</span>
                </div>
              </div>

              {/* Progress */}
              <div className="mt-4">
                <div className="relative h-1.5 w-full overflow-hidden rounded-full bg-white/50 dark:bg-white/[0.08]">
                  <motion.div
                    initial={{ width: 0 }}
                    animate={{ width: `${pct}%` }}
                    transition={{
                      duration: 1.1,
                      delay: 0.85 + i * 0.08,
                      ease: [0.22, 1, 0.36, 1],
                    }}
                    className={cn("h-full rounded-full", t.bar)}
                  />
                </div>
                <div className={cn("mt-2 flex justify-between font-mono text-[10.5px] opacity-80", t.ink)}>
                  <span>
                    {s.progressKm} / {s.distanceKm} km
                  </span>
                  <span>{pct}%</span>
                </div>
              </div>
            </GlassCard>
          );
        })}
      </div>
    </div>
  );
}
