"use client";

import { ArrowUpRight, Compass, PlayCircle, Sparkles } from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";

import { StatusPulse } from "@/components/primitives/status-pulse";
import { HeroLiveMap } from "./hero-live-map";

const ease = [0.22, 1, 0.36, 1] as const;

/* -------------------------------------------------------------------------- */
/* HomeHero — the operator's premium hero on /home.                            */
/* Split layout: greeting + key copy + CTAs on the left, live facility map on  */
/* the right at md+. On mobile, the map drops below the copy so the page       */
/* opens with a quick orientation, then the visual rewards the scroll.         */
/* -------------------------------------------------------------------------- */
export function HomeHero({ firstName }: { firstName: string }) {
  const greeting = useGreeting();
  return (
    <section className="relative">
      <div className="grid grid-cols-1 items-center gap-10 md:grid-cols-12 md:gap-10 lg:gap-12">
        {/* Copy column */}
        <div className="md:col-span-6 lg:col-span-5">
          <motion.div
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.55, ease }}
            className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] backdrop-blur dark:bg-white/[0.05]"
          >
            <StatusPulse tone="success" />
            <span>All operations nominal</span>
            <span className="text-[var(--color-ink-300)]">·</span>
            <span className="font-mono">{nowHHMM()} GMT+7</span>
          </motion.div>

          <motion.h1
            initial={{ opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.65, delay: 0.05, ease }}
            className="mt-5 font-display text-[2.4rem] leading-[1.04] tracking-[-0.035em] font-semibold text-[var(--color-ink-900)] sm:text-[2.75rem] md:text-[3.1rem] lg:text-[3.6rem]"
          >
            {greeting},{" "}
            <span className="text-[var(--color-ink-500)]">{firstName}.</span>
            <br />
            The floor is{" "}
            <span className="relative inline-block">
              <span className="relative z-[1]">live.</span>
              {/* Animated highlight bar that grows in on mount */}
              <motion.span
                initial={{ scaleX: 0 }}
                animate={{ scaleX: 1 }}
                transition={{ duration: 0.7, delay: 0.6, ease }}
                style={{ transformOrigin: "left" }}
                className="absolute -bottom-1 left-0 right-0 h-[0.45em] rounded-full bg-[var(--color-amber)] opacity-70 z-0"
                aria-hidden
              />
            </span>
          </motion.h1>

          <motion.p
            initial={{ opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.65, delay: 0.12, ease }}
            className="mt-5 max-w-md text-[15px] leading-relaxed text-[var(--color-ink-500)] md:text-[16px]"
          >
            Every station, every robot, every order — streaming straight from
            the facility into one control deck. Glance left for the cartography,
            scroll for the rest of the shift.
          </motion.p>

          <motion.div
            initial={{ opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.65, delay: 0.2, ease }}
            className="mt-7 flex flex-wrap items-center gap-3"
          >
            <Link
              href="/delivery-orders/list"
              className="group inline-flex items-center gap-2 rounded-full bg-[var(--color-brand-900)] pl-5 pr-2 py-2 text-[13.5px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_14px_30px_-12px_rgba(14,21,48,0.65)] transition-transform duration-200 hover:-translate-y-0.5 dark:bg-[var(--color-brand-500)]"
            >
              Open dispatch board
              <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
                <ArrowUpRight className="h-4 w-4" strokeWidth={2.5} />
              </span>
            </Link>
            <button
              type="button"
              className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-4 py-2.5 text-[13px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
            >
              <Sparkles className="h-4 w-4 text-[var(--color-brand-500)]" strokeWidth={2} />
              Ask Co-pilot
            </button>
          </motion.div>

          {/* Tiny inline trust strip — gives the hero a little texture without
              shouting numbers at the user (FleetPulseStrip below does that). */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ duration: 0.6, delay: 0.4 }}
            className="mt-8 flex flex-wrap items-center gap-x-5 gap-y-2 text-[11px] font-mono tracking-tight text-[var(--color-ink-400)]"
          >
            <span className="inline-flex items-center gap-1.5">
              <Compass className="h-3 w-3" strokeWidth={2.4} />
              RIOT3
            </span>
            <span className="text-[var(--color-ink-200)]">·</span>
            <span>connected</span>
            <span className="text-[var(--color-ink-200)]">·</span>
            <span>realtime stream</span>
            <span className="text-[var(--color-ink-200)]">·</span>
            <span>v2.4</span>
          </motion.div>
        </div>

        {/* Live map column */}
        <motion.div
          initial={{ opacity: 0, y: 14 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 0.1, ease }}
          className="md:col-span-6 lg:col-span-7"
        >
          <HeroLiveMap />
        </motion.div>
      </div>
    </section>
  );
}

/* -------------------------------------------------------------------------- */
/* Time-aware greeting + clock.                                                */
/* Both are stable across SSR / client by reading from the SAME ref of Date()  */
/* via useState initialiser so React never sees a hydration mismatch.          */
/* -------------------------------------------------------------------------- */
function useGreeting() {
  if (typeof window === "undefined") return "Welcome back";
  const h = new Date().getHours();
  if (h < 5) return "Burning the midnight oil";
  if (h < 12) return "Good morning";
  if (h < 17) return "Good afternoon";
  if (h < 22) return "Good evening";
  return "Late shift";
}

function nowHHMM(): string {
  if (typeof window === "undefined") return "—";
  const d = new Date();
  return `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
}
