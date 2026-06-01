"use client";

import { ArrowUpRight } from "lucide-react";
import { motion, useScroll, useTransform } from "motion/react";
import Link from "next/link";
import { useState } from "react";
import { cn } from "@/lib/utils";

const items = [
  { label: "Home", href: "#top" },
  { label: "Features", href: "#features" },
  { label: "Platforms", href: "#platforms" },
  { label: "Testimonials", href: "#testimonials" },
  { label: "Pricing", href: "#pricing" },
] as const;

export function LandingTopNav() {
  const [active, setActive] = useState<(typeof items)[number]["label"]>("Home");

  // Stay pinned for the first 60 px of scroll, then slide up + fade
  // out by 100 px so the landing content owns the rest of the viewport.
  const { scrollY } = useScroll();
  const opacity = useTransform(scrollY, [60, 100], [1, 0], { clamp: true });
  const driftY = useTransform(scrollY, [60, 100], [0, -24], { clamp: true });
  const pointerEvents = useTransform(scrollY, (v) => (v >= 100 ? "none" : "auto"));

  return (
    <motion.header
      initial={{ opacity: 0, y: -12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
      className="fixed top-4 inset-x-4 z-40"
    >
      <motion.div
        style={{ opacity, y: driftY, pointerEvents }}
        className="relative mx-auto flex max-w-[1240px] items-center gap-2 px-2"
      >
        {/* Wordmark */}
        <Link href="/" className="flex items-center gap-2.5 pr-4">
          <span
            className="relative grid h-9 w-9 place-items-center rounded-full text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.45),0_6px_14px_-4px_rgba(79,93,255,0.6)]"
            style={{
              background:
                "radial-gradient(circle at 30% 25%, #A2AAFF 0%, #6F7BFF 40%, #3441C8 100%)",
            }}
            aria-hidden
          >
            <span className="absolute inset-1 rounded-full border border-white/25" />
            <span
              className="absolute h-1.5 w-1.5 rounded-full bg-white"
              style={{ top: 5, right: 5 }}
            />
          </span>
          <span className="font-display text-[1.05rem] font-semibold tracking-[0.04em] uppercase">
            TMS
          </span>
        </Link>

        {/* Nav items — absolutely centred so neither the logo width nor
            the CTA width pulls them off-axis. */}
        <nav className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 hidden md:flex items-center gap-1">
          {items.map((it) => {
            const isActive = active === it.label;
            return (
              <a
                key={it.label}
                href={it.href}
                onClick={() => setActive(it.label)}
                className={cn(
                  "relative rounded-full px-3.5 py-2 text-[13px] transition-colors",
                  isActive
                    ? "font-semibold text-[var(--color-ink-900)]"
                    : "font-medium text-[var(--color-ink-600)] hover:text-[var(--color-ink-900)] hover:bg-white/40 dark:hover:bg-white/[0.06]",
                )}
              >
                {it.label}
                {isActive && (
                  <motion.span
                    layoutId="landing-nav-underline"
                    transition={{ type: "spring", stiffness: 380, damping: 30 }}
                    className="absolute left-3 right-3 -bottom-0.5 h-[2px] rounded-full bg-[var(--color-brand-500)]"
                  />
                )}
              </a>
            );
          })}
        </nav>

        {/* CTAs */}
        <div className="flex items-center gap-2 ml-auto">
          <Link
            href="/dashboard"
            className="hidden sm:inline-flex items-center rounded-full border border-[var(--color-ink-100)] bg-white/70 px-4 py-2 text-[13px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
          >
            Sign in
          </Link>
          <Link
            href="/dashboard"
            className="group inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] pl-4 pr-1.5 py-1.5 text-[13px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_10px_24px_-10px_rgba(14,21,48,0.6)] transition-transform duration-200 hover:-translate-y-0.5"
          >
            Get started
            <span className="grid h-7 w-7 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
              <ArrowUpRight className="h-3.5 w-3.5" strokeWidth={2.5} />
            </span>
          </Link>
        </div>
      </motion.div>
    </motion.header>
  );
}
