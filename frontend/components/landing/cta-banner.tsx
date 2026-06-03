"use client";

import { ArrowUpRight, Sparkles } from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";

const ease = [0.22, 1, 0.36, 1] as const;

export function CtaBanner() {
  return (
    <section id="pricing" className="mx-auto max-w-[1240px] px-4 pt-24 sm:px-6 md:pt-32">
      <motion.div
        initial={{ opacity: 0, y: 22 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-15%" }}
        transition={{ duration: 0.7, ease }}
        className="glass glass-edge relative overflow-hidden rounded-[var(--radius-2xl)] px-6 py-12 md:p-14"
      >
        {/* Halftone dot pattern (right side) */}
        <div className="halftone absolute inset-y-0 right-0 w-2/3 opacity-50" aria-hidden />

        {/* Floating iridescent shard accent */}
        <motion.div
          animate={{ y: [0, -10, 0], rotate: [0, 8, 0] }}
          transition={{ duration: 8, repeat: Infinity, ease: "easeInOut" }}
          className="absolute -top-8 -right-8 h-48 w-48 rounded-full opacity-70"
          style={{
            background:
              "conic-gradient(from 200deg, #c7d4ff, #e0d4ff, #ffd4e6, #ffe7c7, #c7f0d8, #c7d4ff)",
            boxShadow:
              "inset 0 6px 20px rgba(255,255,255,0.7), 0 30px 60px -10px rgba(79,93,255,0.4)",
            filter: "blur(2px)",
          }}
          aria-hidden
        />

        <div className="relative z-[1] max-w-2xl">
          <span className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] backdrop-blur dark:bg-white/[0.06]">
            <Sparkles className="h-3 w-3" strokeWidth={2.2} />
            30-day free trial · no card required
          </span>
          <h2 className="mt-5 font-display text-[2.25rem] md:text-[3.5rem] leading-[1.02] tracking-[-0.035em] font-semibold text-[var(--color-ink-900)]">
            Ready to{" "}
            <span className="text-[var(--color-ink-500)]">stop firefighting?</span>
          </h2>
          <p className="mt-4 max-w-lg text-[15.5px] leading-relaxed text-[var(--color-ink-500)]">
            Spin up your first dispatch in 9 minutes. Migrate from your
            spreadsheets in an afternoon. Cancel any time — but you won&apos;t.
          </p>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            <Link
              href="/login?mode=signup"
              className="group inline-flex items-center gap-2 rounded-full bg-[var(--color-brand-900)] pl-5 pr-2 py-2 text-[14px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_14px_30px_-12px_rgba(14,21,48,0.65)] transition-transform duration-200 hover:-translate-y-0.5"
            >
              Start free trial
              <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
                <ArrowUpRight className="h-4 w-4" strokeWidth={2.5} />
              </span>
            </Link>
            <a
              href="#"
              className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-4 py-2.5 text-[13.5px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white dark:bg-white/[0.05] dark:hover:bg-white/[0.12]"
            >
              Talk to sales
            </a>
          </div>
        </div>
      </motion.div>
    </section>
  );
}
