"use client";

import { ArrowUpRight, Plus, Sparkles } from "lucide-react";
import { motion } from "motion/react";
import { useAuth } from "@/components/auth/auth-provider";
import { StatusPulse } from "@/components/primitives/status-pulse";

export function GreetingStrip() {
  const { user } = useAuth();
  const firstName = user?.displayName?.split(" ")[0] || "there";
  return (
    <motion.section
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, ease: [0.22, 1, 0.36, 1], delay: 0.1 }}
      className="mt-2 mb-7 flex flex-col gap-5 md:flex-row md:items-end md:justify-between"
    >
      <div className="min-w-0">
        <div className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] dark:bg-white/[0.04]">
          <StatusPulse tone="success" />
          <span>All operations nominal</span>
          <span className="text-[var(--color-ink-300)]">·</span>
          <span className="font-mono text-[var(--color-ink-500)]">07:42 GMT+7</span>
        </div>
        <h1 className="mt-3 font-display text-[2.5rem] leading-[1.05] font-semibold tracking-[-0.03em] text-[var(--color-ink-900)] md:text-[3rem]">
          Good evening, <span className="text-[var(--color-ink-500)]">{firstName}</span>
        </h1>
        <p className="mt-2 max-w-xl text-[15px] leading-relaxed text-[var(--color-ink-500)]">
          Three high-priority dispatches need your review before the Eastern Seaboard window
          closes at <span className="text-[var(--color-ink-700)] font-medium">22:00</span>.
        </p>
      </div>

      <div className="flex items-center gap-2.5">
        <button className="group inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-4 py-2.5 text-[13px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]">
          <Sparkles className="h-4 w-4 text-[var(--color-brand-500)]" strokeWidth={2} />
          Ask Co-pilot
        </button>
        <button className="group inline-flex items-center gap-2 rounded-full bg-[var(--color-brand-900)] pl-4 pr-2 py-2 text-[13px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_10px_24px_-10px_rgba(14,21,48,0.6)] transition-transform duration-200 hover:-translate-y-0.5 cursor-pointer">
          <Plus className="h-4 w-4" strokeWidth={2.5} />
          New dispatch
          <span className="ml-1 grid h-7 w-7 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
            <ArrowUpRight className="h-4 w-4" strokeWidth={2.5} />
          </span>
        </button>
      </div>
    </motion.section>
  );
}
