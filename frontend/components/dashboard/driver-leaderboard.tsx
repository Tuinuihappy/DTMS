"use client";

import { Award, TrendingDown, TrendingUp } from "lucide-react";
import { motion } from "motion/react";
import { Avatar } from "@/components/primitives/avatar";
import { GlassCard } from "@/components/primitives/glass-card";
import { SectionLabel } from "@/components/primitives/section-label";
import { topDrivers } from "@/lib/mock-data";
import { spring, stagger, T } from "@/lib/motion";
import { cn } from "@/lib/utils";
import { useFirstVisit } from "./use-first-visit";

export function DriverLeaderboard() {
  const first = useFirstVisit("dashboard");
  return (
    <GlassCard
      variant="default"
      className="col-span-12 md:col-span-6 lg:col-span-4 p-6"
      initial={first ? { opacity: 0, y: 12 } : false}
      animate={{ opacity: 1, y: 0 }}
      transition={{ ...T.slow, delay: 0.12 }}
    >
      <SectionLabel
        icon={<Award className="h-5 w-5" strokeWidth={2} />}
        title="Driver leaderboard"
        action={
          // No full leaderboard page exists yet — an inert control instead
          // of href="#", which jumped the dashboard to the top.
          <button
            type="button"
            disabled
            title="The full leaderboard isn't available yet"
            className="text-[12px] font-medium text-[var(--color-ink-500)] disabled:cursor-not-allowed disabled:opacity-60"
          >
            See all
          </button>
        }
      />

      <ul className="mt-6 divide-y divide-[var(--color-ink-100)]">
        {topDrivers.map((d, i) => {
          const positive = d.trend >= 0;
          return (
            // `layout` + stable keys: when the ranking changes, rows glide
            // to their new place instead of jumping.
            <motion.li
              key={d.name}
              layout="position"
              initial={first ? { opacity: 0, x: -8 } : false}
              animate={{ opacity: 1, x: 0 }}
              transition={{ ...T.base, delay: 0.14 + stagger(i, 0.025), layout: spring.gentle }}
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
