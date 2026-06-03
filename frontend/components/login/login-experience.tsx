"use client";

import { ArrowLeft } from "lucide-react";
import { motion } from "motion/react";
import Link from "next/link";
import { Suspense } from "react";
import { AuthPanel } from "./auth-panel";
import { DuskScene } from "./dusk-scene";

/* -------------------------------------------------------------------------- */
/* LoginExperience — full-bleed page that combines the cinematic dusk scene   */
/* with the floating glass auth panel.                                        */
/*                                                                            */
/* Desktop (lg+): two columns, scene left, panel right.                       */
/* Tablet / mobile: stacked — scene becomes a 280-380px hero band at top,    */
/* form takes the rest of the viewport. The scene stays full-bleed even when  */
/* stacked so the dusk drama survives at narrow widths.                       */
/* -------------------------------------------------------------------------- */

const EASE_OUT_QUART = [0.22, 1, 0.36, 1] as const;

export function LoginExperience() {
  return (
    <main className="dusk-root relative min-h-svh w-full overflow-hidden bg-[#0A0420] text-amber-50">
      {/* DESKTOP LAYOUT — side-by-side, dusk scene as the left half ─────── */}
      <div className="hidden lg:grid h-svh grid-cols-[1.15fr_1fr]">
        {/* LEFT — cinematic scene + display copy overlay */}
        <div className="relative">
          <DuskScene />
          <DisplayCopy variant="desktop" />
          <TopBar variant="overlay" />
        </div>
        {/* RIGHT — auth panel sitting over a deep velvet field */}
        <div
          className="relative grid place-items-center px-10"
          style={{
            background:
              "radial-gradient(120% 80% at 20% 0%, #2A1248 0%, #14082C 55%, #0A0420 100%)",
          }}
        >
          <Suspense fallback={null}>
            <AuthPanel />
          </Suspense>
          {/* Tiny corner ornament — a thin sun-glow stripe like cabin trim */}
          <span
            aria-hidden
            className="pointer-events-none absolute left-0 top-1/2 h-40 w-px -translate-y-1/2"
            style={{
              background:
                "linear-gradient(180deg, transparent, rgba(255,200,150,0.4), transparent)",
            }}
          />
        </div>
      </div>

      {/* MOBILE / TABLET LAYOUT — stacked ───────────────────────────────── */}
      <div className="lg:hidden flex min-h-svh flex-col">
        {/* Hero band */}
        <div className="relative h-[58svh] min-h-[420px]">
          <DuskScene />
          <DisplayCopy variant="mobile" />
          <TopBar variant="overlay" />
        </div>
        {/* Form */}
        <div
          className="relative -mt-10 flex-1 px-5 pb-10 pt-8 sm:px-8"
          style={{
            background:
              "linear-gradient(180deg, #14082C 0%, #0A0420 70%, #06031A 100%)",
          }}
        >
          {/* Top edge fade — blends the hero band into the form half */}
          <div
            aria-hidden
            className="pointer-events-none absolute inset-x-0 -top-12 h-12"
            style={{
              background:
                "linear-gradient(180deg, transparent 0%, rgba(20, 8, 44, 0.7) 60%, #14082C 100%)",
            }}
          />
          <div className="mx-auto w-full max-w-[460px]">
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
/* Display copy — "MOVE / DELIVER / ARRIVE" stacked, big Fraunces italic.     */
/* Three-word stagger lands while the sun is mid-rise so the eye is led      */
/* from the sun upward to the words and then back down to the highway.       */
/* -------------------------------------------------------------------------- */

const WORDS = ["Move.", "Deliver.", "Arrive."] as const;

function DisplayCopy({ variant }: { variant: "desktop" | "mobile" }) {
  const base = variant === "desktop"
    ? "absolute left-10 bottom-16 xl:left-14 xl:bottom-20 max-w-[640px]"
    : "absolute left-5 sm:left-7 bottom-10 right-5 sm:right-7";

  return (
    <div className={base}>
      <motion.p
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.7, delay: 0.45, ease: EASE_OUT_QUART }}
        className="font-dusk-body text-[11px] tracking-[0.4em] uppercase text-amber-100/75 mb-4 sm:mb-5"
      >
        <span
          className="inline-block h-px w-8 align-middle mr-3"
          style={{ background: "rgba(255, 220, 170, 0.6)" }}
        />
        Logistics, at dusk
      </motion.p>

      <h1 className="font-dusk-display text-amber-50">
        {WORDS.map((w, i) => (
          <motion.span
            key={w}
            initial={{ opacity: 0, y: 22, filter: "blur(10px)" }}
            animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
            transition={{
              duration: 0.85,
              delay: 0.55 + i * 0.14,
              ease: EASE_OUT_QUART,
            }}
            className={
              variant === "desktop"
                ? "block leading-[0.95] text-[clamp(60px,7vw,120px)] font-semibold tracking-[-0.03em]"
                : "block leading-[0.96] text-[clamp(42px,10.5vw,76px)] font-semibold tracking-[-0.03em]"
            }
            style={{
              textShadow:
                "0 6px 24px rgba(0,0,0,0.4), 0 0 60px rgba(255, 170, 120, 0.18)",
            }}
          >
            {w}
          </motion.span>
        ))}
      </h1>

      <motion.p
        initial={{ opacity: 0, y: 10 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.7, delay: 1.05, ease: EASE_OUT_QUART }}
        className={
          variant === "desktop"
            ? "mt-6 max-w-[480px] font-dusk-body text-[16.5px] leading-relaxed text-amber-50/80"
            : "mt-4 max-w-[420px] font-dusk-body text-[14px] leading-relaxed text-amber-50/75"
        }
      >
        Where freight finds its line. Dispatch, live activity and driver
        comms — one calm console for the long haul.
      </motion.p>
    </div>
  );
}

/* -------------------------------------------------------------------------- */
/* TopBar — minimal "back to home" link sitting in the top-left of the scene  */
/* on desktop, and as a subtle pill at the top of the hero on mobile.         */
/* -------------------------------------------------------------------------- */

function TopBar({ variant }: { variant: "overlay" }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: -8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.6, delay: 0.35, ease: EASE_OUT_QUART }}
      className="absolute left-5 top-5 sm:left-7 sm:top-7"
    >
      <Link
        href="/"
        className="group inline-flex items-center gap-1.5 rounded-full bg-white/[0.07] px-3 py-1.5 font-dusk-body text-[11.5px] font-medium tracking-tight text-amber-50/80 ring-1 ring-white/[0.12] backdrop-blur-md transition-all hover:bg-white/[0.13] hover:text-amber-50"
        aria-label="Back to home"
      >
        <ArrowLeft className="h-3.5 w-3.5 transition-transform group-hover:-translate-x-0.5" strokeWidth={2.2} />
        Home
      </Link>
    </motion.div>
  );
}
