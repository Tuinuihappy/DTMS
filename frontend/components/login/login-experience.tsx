"use client";

import { ArrowLeft } from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";
import { Suspense } from "react";
import { AuthPanel } from "./auth-panel";
import { BrandScene } from "./brand-scene";

/* -------------------------------------------------------------------------- */
/* LoginExperience — full-page /login route.                                   */
/*                                                                            */
/* Theme-aware via the existing canvas + glass tokens. No hard-coded colors    */
/* — the page sits on the global pastel-aurora canvas in light mode and the    */
/* deep-navy canvas in dark mode, with the form panel rendered through        */
/* .glass utilities so it matches every other card in the app.                 */
/*                                                                            */
/* Desktop (lg+): two columns, BrandScene on the left, form on the right.      */
/* Tablet / mobile: stacked — scene shrinks to a compact hero band, form       */
/* takes the rest. Animations gracefully scale.                                */
/* -------------------------------------------------------------------------- */

const ease = [0.22, 1, 0.36, 1] as const;

export function LoginExperience() {
  return (
    <main className="layer-content relative min-h-svh w-full overflow-x-clip">
      <TopBar />

      {/* DESKTOP — side-by-side ─────────────────────────────────────────── */}
      <div className="hidden lg:grid min-h-svh grid-cols-[1.05fr_1fr] xl:grid-cols-[1.15fr_1fr]">
        <div className="relative">
          <BrandScene />
        </div>
        <div className="relative grid place-items-center px-8 py-16 xl:px-12">
          <Suspense fallback={null}>
            <AuthPanel />
          </Suspense>
        </div>
      </div>

      {/* MOBILE / TABLET — stacked ──────────────────────────────────────── */}
      <div className="lg:hidden flex min-h-svh flex-col">
        {/* Compact brand band — hides the constellation orbit (which needs
            wide canvas) but keeps the eyebrow + heading + trust footnote. */}
        <div className="relative pt-20">
          <CompactBrand />
        </div>
        {/* Form */}
        <div className="relative flex-1 px-5 pb-12 pt-6 sm:px-8">
          <div className="mx-auto w-full max-w-[440px]">
            <Suspense fallback={null}>
              <AuthPanel />
            </Suspense>
          </div>
        </div>
      </div>
    </main>
  );
}

/* -------------------------------------------------------------------------- */
/* TopBar — minimal "back home" link in the page corner. Same glass-pill      */
/* shape used by the dashboard's icon buttons so it reads like part of the    */
/* shell, not the login.                                                       */
/* -------------------------------------------------------------------------- */

function TopBar() {
  return (
    <motion.div
      initial={{ opacity: 0, y: -8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.15, ease }}
      className="absolute left-5 top-5 z-20 sm:left-7 sm:top-7"
    >
      <Link
        href="/"
        className="group inline-flex items-center gap-1.5 rounded-full border border-[var(--color-ink-100)] bg-white/70 px-3 py-1.5 text-[12px] font-medium tracking-tight text-[var(--color-ink-700)] backdrop-blur transition-all hover:-translate-y-px hover:bg-white dark:border-white/[0.06] dark:bg-white/[0.05] dark:hover:bg-white/[0.1]"
        aria-label="Back to home"
      >
        <ArrowLeft className="h-3.5 w-3.5 transition-transform group-hover:-translate-x-0.5" strokeWidth={2.2} />
        Home
      </Link>
    </motion.div>
  );
}

/* -------------------------------------------------------------------------- */
/* CompactBrand — mobile / tablet replacement for BrandScene's wide layout.   */
/* Keeps the eyebrow + headline + paragraph + truck mark in a smaller stack.  */
/* -------------------------------------------------------------------------- */

const WORDS = ["Move.", "Deliver.", "Arrive."] as const;

function CompactBrand() {
  return (
    <div className="relative mx-auto max-w-[480px] px-5 pb-2 sm:px-7">
      <motion.div
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.55, delay: 0.2, ease }}
        className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/65 px-3 py-1.5 text-[11px] font-medium text-[var(--color-ink-600)] backdrop-blur dark:border-white/[0.06] dark:bg-white/[0.05]"
      >
        <span className="relative flex h-1.5 w-1.5">
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-[var(--color-success)] opacity-75" />
          <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-[var(--color-success)]" />
        </span>
        Welcome to the operations deck
      </motion.div>

      <h1 className="mt-5 font-display text-[2.25rem] leading-[1.02] tracking-[-0.035em] font-semibold text-[var(--color-ink-900)] sm:text-[2.75rem]">
        {WORDS.map((w, i) => (
          <motion.span
            key={w}
            initial={{ opacity: 0, y: 14, filter: "blur(8px)" }}
            animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
            transition={{ duration: 0.7, delay: 0.3 + i * 0.12, ease }}
            className="inline-block mr-2"
          >
            {w}
          </motion.span>
        ))}
      </h1>

      <motion.p
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.55, delay: 0.82, ease }}
        className="mt-4 max-w-[380px] text-[14px] leading-relaxed text-[var(--color-ink-500)]"
      >
        Live fleet activity, dispatch funnels, driver comms — sign in to take the wheel.
      </motion.p>
    </div>
  );
}
