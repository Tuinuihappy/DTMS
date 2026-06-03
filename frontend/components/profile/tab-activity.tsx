"use client";

import { CheckCircle2, FileText, LogIn, PackageCheck, Truck } from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

type Event = {
  icon: React.ReactNode;
  title: string;
  detail: string;
  time: string;
  tone: "brand" | "success" | "amber" | "ink";
};

const events: Event[] = [
  {
    icon: <PackageCheck className="h-4 w-4" strokeWidth={2.2} />,
    title: "Dispatch #A-2841 delivered",
    detail: "On-time · cold-chain integrity verified",
    time: "12 min ago",
    tone: "success",
  },
  {
    icon: <Truck className="h-4 w-4" strokeWidth={2.2} />,
    title: "Truck TMS · 8842 departed Laem Chabang",
    detail: "Route LCB → BKK-NE · ETA 04:42",
    time: "1h 22m ago",
    tone: "brand",
  },
  {
    icon: <FileText className="h-4 w-4" strokeWidth={2.2} />,
    title: "Hazmat manifest signed",
    detail: "Consignment #H-118 · Class 3 flammable liquid",
    time: "3h ago",
    tone: "amber",
  },
  {
    icon: <CheckCircle2 className="h-4 w-4" strokeWidth={2.2} />,
    title: "Pre-trip inspection cleared",
    detail: "Volvo FH16 · all 24 checkpoints green",
    time: "5h ago",
    tone: "success",
  },
  {
    icon: <LogIn className="h-4 w-4" strokeWidth={2.2} />,
    title: "Shift started",
    detail: "Clocked in at DC-04 control room",
    time: "Today 06:12",
    tone: "ink",
  },
];

const toneRing: Record<Event["tone"], string> = {
  brand:
    "from-[var(--color-pastel-sky)] to-[#c7d4ff] text-[var(--color-brand-900)] dark:to-[#2a3a7a] dark:text-[var(--color-pastel-sky-ink)]",
  success:
    "from-[var(--color-success-soft)] to-[#b6e8cf] text-[var(--color-success)] dark:to-[#1f5a40] dark:text-[var(--color-success)]",
  amber:
    "from-[var(--color-amber-soft)] to-[#fcd398] text-[#8a4a07] dark:to-[#6a4a1c] dark:text-[var(--color-amber)]",
  ink: "from-[var(--color-ink-100)] to-[#cfd6e6] text-[var(--color-ink-800)] dark:to-[#3a4870] dark:text-[var(--color-ink-700)]",
};

export function TabActivity() {
  return (
    <GlassCard
      variant="default"
      className="p-7"
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: EASE }}
    >
      <div className="flex items-center justify-between">
        <div>
          <span className="text-[10.5px] font-bold uppercase tracking-[0.18em] text-[var(--color-ink-400)]">
            Recent activity
          </span>
          <h3 className="mt-2 font-display text-[1.3rem] font-semibold tracking-[-0.02em] text-[var(--color-ink-900)]">
            Today's timeline
          </h3>
        </div>
        <button className="rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] transition-colors hover:bg-white cursor-pointer dark:border-white/10 dark:bg-white/[0.04] dark:hover:bg-white/[0.08]">
          View full log
        </button>
      </div>

      <ol className="mt-6 relative">
        {/* Vertical thread */}
        <span
          aria-hidden
          className="absolute left-[19px] top-2 bottom-2 w-px bg-gradient-to-b from-[var(--color-ink-100)] via-[var(--color-ink-100)] to-transparent dark:from-white/10 dark:via-white/10"
        />

        {events.map((e, i) => (
          <motion.li
            key={i}
            initial={{ opacity: 0, x: -12 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.5, delay: 0.08 + i * 0.07, ease: EASE }}
            className="relative pl-14 pb-5 last:pb-0"
          >
            <span
              className={`absolute left-0 top-0 grid h-10 w-10 place-items-center rounded-2xl bg-gradient-to-br ${toneRing[e.tone]} shadow-[inset_0_1px_0_rgba(255,255,255,0.8),0_6px_14px_-6px_rgba(15,23,42,0.18)]`}
            >
              {e.icon}
            </span>

            <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
              <p className="text-[14px] font-semibold text-[var(--color-ink-900)]">{e.title}</p>
              <span className="font-mono text-[11.5px] text-[var(--color-ink-500)]">{e.time}</span>
            </div>
            <p className="mt-0.5 text-[13px] text-[var(--color-ink-600)]">{e.detail}</p>
          </motion.li>
        ))}
      </ol>
    </GlassCard>
  );
}
