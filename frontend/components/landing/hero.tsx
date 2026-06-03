"use client";

import { ArrowUpRight, PlayCircle, Truck } from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { FauxThreeDVisual } from "./faux-3d-visual";

const ease = [0.22, 1, 0.36, 1] as const;

export function LandingHero() {
  return (
    <section className="relative mx-auto max-w-[1240px] px-4 pt-28 sm:px-6 md:pt-32">
      <div className="grid grid-cols-1 gap-12 md:grid-cols-12 md:gap-8 items-center">
        {/* Text column */}
        <div className="md:col-span-7">
          <motion.div
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, ease }}
            className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] backdrop-blur dark:bg-white/[0.05]"
          >
            <StatusPulse tone="success" />
            <span>Now live in 14 countries</span>
            <span className="text-[var(--color-ink-300)]">·</span>
            <span className="font-mono">v2.4</span>
          </motion.div>

          <motion.h1
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.7, delay: 0.05, ease }}
            className="mt-6 font-display text-[2.75rem] leading-[1.02] tracking-[-0.035em] font-semibold text-[var(--color-ink-900)] md:text-[4.25rem]"
          >
            Move every truck.{" "}
            <span className="inline-flex items-center gap-2 align-baseline">
              <span
                className="inline-grid h-[0.9em] w-[0.9em] place-items-center rounded-[28%] text-white"
                style={{
                  background:
                    "conic-gradient(from 45deg, #c7d4ff, #e0d4ff, #ffd4e6, #ffe7c7, #d4f0ff, #c7d4ff)",
                  boxShadow:
                    "inset 0 3px 8px rgba(255,255,255,0.9), 0 6px 14px -4px rgba(79,93,255,0.45)",
                  transform: "rotate(-8deg)",
                }}
                aria-hidden
              >
                <Truck className="h-[55%] w-[55%] text-[var(--color-brand-900)]" strokeWidth={2.2} />
              </span>
            </span>
            <br />
            Hit every window.
          </motion.h1>

          <motion.p
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.7, delay: 0.12, ease }}
            className="mt-6 max-w-xl text-[15.5px] leading-relaxed text-[var(--color-ink-500)] md:text-[17px]"
          >
            TMS is the operations control deck for industrial logistics —
            live fleet tracking, dispatch funnels that actually convert,
            driver performance, and SLA-tight comms in a single dashboard
            your team will use on day one.
          </motion.p>

          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.7, delay: 0.2, ease }}
            className="mt-8 flex flex-wrap items-center gap-3"
          >
            <Link
              href="/login?mode=signup"
              className="group inline-flex items-center gap-2 rounded-full bg-[var(--color-brand-900)] pl-5 pr-2 py-2 text-[14px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_14px_30px_-12px_rgba(14,21,48,0.65)] transition-transform duration-200 hover:-translate-y-0.5"
            >
              Start free trial
              <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
                <ArrowUpRight className="h-4 w-4" strokeWidth={2.5} />
              </span>
            </Link>
            <button className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-4 py-2.5 text-[13.5px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]">
              <PlayCircle className="h-4 w-4" strokeWidth={2.2} />
              Watch the 90s demo
            </button>
          </motion.div>

          {/* Mini trust strip */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.7, delay: 0.35 }}
            className="mt-10 flex flex-wrap items-center gap-x-7 gap-y-3 text-[11px] uppercase tracking-[0.14em] font-medium text-[var(--color-ink-400)]"
          >
            <span>Trusted by</span>
            <span className="font-display normal-case tracking-tight font-semibold text-[var(--color-ink-700)] text-[15px]">
              Delta Logistics
            </span>
          </motion.div>
        </div>

        {/* Visual column */}
        <motion.div
          initial={{ opacity: 0, scale: 0.92 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ duration: 0.9, delay: 0.15, ease }}
          className="md:col-span-5"
        >
          <FauxThreeDVisual />
        </motion.div>
      </div>
    </section>
  );
}
