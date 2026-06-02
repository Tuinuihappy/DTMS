"use client";

import { Award, TrendingDown, TrendingUp } from "lucide-react";
import { motion } from "motion/react";
import { Avatar } from "@/components/primitives/avatar";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { topDrivers } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

export function DriverLeaderboard() {
  return (
    <GlassCard
      variant="default"
      className="col-span-12 md:col-span-6 lg:col-span-4 p-6"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.65, ease: [0.22, 1, 0.36, 1] }}
    >
      <SectionLabel
        icon={<Award className="h-5 w-5" strokeWidth={2} />}
        title="Driver leaderboard"
        action={
          <a
            href="#"
            className="text-[12px] font-medium text-[var(--color-ink-500)] underline-offset-4 hover:underline hover:text-[var(--color-ink-800)]"
          >
            See all
          </a>
        }
      />

      <ul className="mt-6 divide-y divide-[var(--color-ink-100)]">
        {topDrivers.map((d, i) => {
          const positive = d.trend >= 0;
          return (
            <motion.li
              key={d.name}
              initial={{ opacity: 0, x: -8 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.4, delay: 0.85 + i * 0.05 }}
              className="flex items-center gap-3.5 py-3.5 first:pt-2 last:pb-1"
            >
              <span className="font-mono text-[11px] font-semibold w-5 text-[var(--color-ink-300)] tabular-nums">
                {String(i + 1).padStart(2, "0")}
              </span>
              <Avatar name={d.name} hue={d.avatarHue} size="md" />
              <div className="min-w-0 flex-1">
                <div className="text-[13.5px] font-semibold text-[var(--color-ink-900)] truncate">
                  {d.name}
                </div>
                <div className="text-[11.5px] text-[var(--color-ink-500)] truncate">
                  {d.role} · {d.region}
                </div>
              </div>
              <div className="text-right">
                <div className="font-mono text-[14px] font-semibold text-[var(--color-ink-900)]">
                  {d.score}
                </div>
                <div
                  className={cn(
                    "mt-0.5 inline-flex items-center gap-0.5 text-[10.5px] font-semibold",
                    positive ? "text-[var(--color-success)]" : "text-[var(--color-coral)]",
                  )}
                >
                  {positive ? (
                    <TrendingUp className="h-2.5 w-2.5" strokeWidth={2.6} />
                  ) : (
                    <TrendingDown className="h-2.5 w-2.5" strokeWidth={2.6} />
                  )}
                  {positive ? "+" : ""}
                  {d.trend.toFixed(1)}%
                </div>
              </div>
            </motion.li>
          );
        })}
      </ul>
    </GlassCard>
  );
}
