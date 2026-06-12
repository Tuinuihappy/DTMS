"use client";

import {
  ArrowUpRight,
  Bot,
  Calendar,
  ClipboardList,
  Headphones,
} from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";

import { GlassCard } from "@/components/primitives/glass-card";
import { cn } from "@/lib/utils";

const ease = [0.22, 1, 0.36, 1] as const;

type Action = {
  label: string;
  blurb: string;
  href: string;
  tone: "pastel-sky" | "pastel-lavender" | "pastel-mint" | "pastel-peach";
  icon: React.ReactNode;
  hint: string;
};

const actions: Action[] = [
  {
    label: "Delivery orders",
    blurb: "Browse, edit, and dispatch the active order book.",
    href: "/delivery-orders/list",
    tone: "pastel-sky",
    icon: <ClipboardList className="h-5 w-5" strokeWidth={2.1} />,
    hint: "+ create",
  },
  {
    label: "Order templates",
    blurb: "Reusable workflows for repeat dispatch patterns.",
    href: "/delivery-orders/order-templates",
    tone: "pastel-lavender",
    icon: <Calendar className="h-5 w-5" strokeWidth={2.1} />,
    hint: "library",
  },
  {
    label: "Action templates",
    blurb: "Atomic robot actions that compose into orders.",
    href: "/delivery-orders/action-templates",
    tone: "pastel-mint",
    icon: <Bot className="h-5 w-5" strokeWidth={2.1} />,
    hint: "atomic",
  },
  {
    label: "Driver comms",
    blurb: "Push-to-talk + transcribed voice across every shift.",
    href: "/dashboard",
    tone: "pastel-peach",
    icon: <Headphones className="h-5 w-5" strokeWidth={2.1} />,
    hint: "live",
  },
];

/* -------------------------------------------------------------------------- */
/* QuickActions — 4-card jump grid for the operator's most common landings.   */
/* Each card uses GlassCard's pastel variant so the row reads as a colour     */
/* spectrum across the page, not four identical tiles.                       */
/* -------------------------------------------------------------------------- */
export function QuickActions() {
  return (
    <section className="mt-16 md:mt-20">
      <motion.div
        initial={{ opacity: 0, y: 14 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-15%" }}
        transition={{ duration: 0.55, ease }}
        className="flex items-end justify-between gap-4"
      >
        <div>
          <span className="text-[10.5px] uppercase tracking-[0.14em] font-semibold text-[var(--color-ink-400)]">
            Jump in
          </span>
          <h2 className="mt-1 font-display text-[1.6rem] leading-tight tracking-[-0.025em] font-semibold text-[var(--color-ink-900)] md:text-[1.9rem]">
            Where the shift goes next.
          </h2>
        </div>
        <Link
          href="/dashboard"
          className="hidden sm:inline-flex items-center gap-1 rounded-full bg-white/60 px-3 py-1.5 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white cursor-pointer dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
        >
          Full dashboard
          <ArrowUpRight className="h-3.5 w-3.5" strokeWidth={2.4} />
        </Link>
      </motion.div>

      <div className="mt-7 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {actions.map((a, i) => (
          <motion.div
            key={a.label}
            initial={{ opacity: 0, y: 16 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true, margin: "-10%" }}
            transition={{ duration: 0.5, delay: 0.06 * i, ease }}
          >
            <Link href={a.href} className="block group">
              <GlassCard
                variant={a.tone}
                interactive
                className={cn(
                  "p-5 h-full flex flex-col gap-3 transition-transform duration-300 group-hover:-translate-y-1",
                )}
              >
                <div className="flex items-start justify-between">
                  <span className="grid h-10 w-10 place-items-center rounded-[12px] bg-white/70 text-[var(--color-ink-800)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_10px_-4px_rgba(15,23,42,0.12)] dark:bg-white/[0.08]">
                    {a.icon}
                  </span>
                  <span className="font-mono text-[10px] tracking-[0.12em] uppercase opacity-65">
                    {a.hint}
                  </span>
                </div>
                <div className="flex items-end justify-between gap-2 mt-auto">
                  <div className="min-w-0">
                    <div className="font-display text-[15px] font-semibold tracking-tight">
                      {a.label}
                    </div>
                    <p className="mt-1 text-[12.5px] leading-snug opacity-80">
                      {a.blurb}
                    </p>
                  </div>
                  <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-ink-900)]/95 text-white transition-transform duration-300 group-hover:rotate-45 group-hover:bg-[var(--color-ink-900)]">
                    <ArrowUpRight className="h-4 w-4" strokeWidth={2.5} />
                  </span>
                </div>
              </GlassCard>
            </Link>
          </motion.div>
        ))}
      </div>
    </section>
  );
}
