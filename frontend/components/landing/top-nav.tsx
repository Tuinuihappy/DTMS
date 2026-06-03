"use client";

import { ArrowUpRight, Menu, Truck, X } from "lucide-react";
import { AnimatePresence, motion, useScroll, useTransform } from "motion/react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

const items = [
  { label: "Home", href: "#top" },
  { label: "Features", href: "#features" },
  { label: "Platforms", href: "#platforms" },
  { label: "Testimonials", href: "#testimonials" },
  { label: "Pricing", href: "#pricing" },
  { label: "Contact", href: "#contact" },
] as const;

export function LandingTopNav() {
  const [active, setActive] = useState<string>("Home");
  const [mobileOpen, setMobileOpen] = useState(false);

  // Stay pinned for the first 60 px of scroll, then slide up + fade
  // out by 100 px so the landing content owns the rest of the viewport.
  const { scrollY } = useScroll();
  const opacity = useTransform(scrollY, [60, 100], [1, 0], { clamp: true });
  const driftY = useTransform(scrollY, [60, 100], [0, -24], { clamp: true });
  const pointerEvents = useTransform(scrollY, (v) => (v >= 100 ? "none" : "auto"));

  // Close the mobile menu on ESC + lock body scroll so the background
  // doesn't scroll behind the overlay (and so layout shifts don't push
  // content around when the menu opens).
  useEffect(() => {
    if (!mobileOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setMobileOpen(false);
    };
    window.addEventListener("keydown", onKey);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = prevOverflow;
    };
  }, [mobileOpen]);

  return (
    <>
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
            className="relative grid h-9 w-9 place-items-center rounded-[10px] text-[var(--color-ink-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.6),0_6px_14px_-4px_rgba(15,23,42,0.18)]"
            style={{
              background:
                "linear-gradient(135deg, #FFD8CC 0%, #F3D5EC 55%, #D7DBFF 100%)",
            }}
            aria-hidden
          >
            <Truck className="h-[22px] w-[22px]" strokeWidth={1.75} />
          </span>
          <span className="font-display text-[1.05rem] font-semibold tracking-[0.04em] uppercase">
            TMS
          </span>
        </Link>

        {/* Nav items — absolutely centred so neither the logo width nor
            the CTA width pulls them off-axis. Hidden below lg (mobile
            menu takes over up through tablet portrait). */}
        <nav className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 hidden lg:flex items-center gap-1">
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

        {/* Right side — CTAs + mobile menu toggle */}
        <div className="flex items-center gap-2 ml-auto">
          <Link
            href="/login"
            className="hidden sm:inline-flex items-center rounded-full border border-[var(--color-ink-100)] bg-white/70 px-4 py-2 text-[13px] font-medium text-[var(--color-ink-700)] backdrop-blur transition-colors hover:bg-white dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
          >
            Sign in
          </Link>
          <Link
            href="/login?mode=signup"
            aria-label="Get started"
            className="group inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] py-1.5 pl-1.5 sm:pl-4 pr-1.5 text-[13px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_10px_24px_-10px_rgba(14,21,48,0.6)] transition-transform duration-200 hover:-translate-y-0.5"
          >
            <span className="hidden sm:inline">Get started</span>
            <span className="grid h-7 w-7 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] transition-transform duration-300 group-hover:rotate-45">
              <ArrowUpRight className="h-3.5 w-3.5" strokeWidth={2.5} />
            </span>
          </Link>

          {/* Mobile menu toggle — visible up through tablet portrait (<lg) */}
          <button
            type="button"
            aria-label={mobileOpen ? "Close menu" : "Open menu"}
            aria-expanded={mobileOpen}
            onClick={() => setMobileOpen((v) => !v)}
            className="lg:hidden grid h-10 w-10 place-items-center rounded-full bg-white/70 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.8),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:bg-white hover:text-[var(--color-ink-900)] cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
          >
            {mobileOpen ? (
              <X className="h-[18px] w-[18px]" strokeWidth={2} />
            ) : (
              <Menu className="h-[18px] w-[18px]" strokeWidth={2} />
            )}
          </button>
        </div>

      </motion.div>
    </motion.header>

    {/* Mobile menu — backdrop + floating card panel, rendered as TOP-LEVEL
        siblings (not wrapped in a shared motion.div). Wrapping them in a
        common ancestor with framer-motion opacity creates a stacking context
        that isolates the panel's `.glass` backdrop-filter, making the blur
        fail to obscure the page behind it. */}
    <AnimatePresence>
      {mobileOpen && (
        <motion.button
          key="mobile-backdrop"
          type="button"
          aria-label="Close menu"
          onClick={() => setMobileOpen(false)}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
          className="lg:hidden fixed inset-0 z-40 bg-[var(--color-ink-900)]/30 backdrop-blur-2xl cursor-default"
        />
      )}
    </AnimatePresence>

    <AnimatePresence>
      {mobileOpen && (
        <motion.nav
          key="mobile-menu"
          initial={{ opacity: 0, y: -8 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -8 }}
          transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
          className="lg:hidden fixed top-[68px] left-1/2 -translate-x-1/2 z-50 w-[calc(100vw-4rem)] rounded-[var(--radius-xl)] bg-white dark:bg-[var(--color-ink-950)] border border-[var(--color-ink-100)] dark:border-white/[0.06] p-2 flex flex-col gap-0.5 shadow-[0_18px_50px_-18px_rgba(15,23,42,0.45)]"
          aria-label="Mobile navigation"
        >
          {items.map((it) => {
            const isActive = active === it.label;
            return (
              <a
                key={it.label}
                href={it.href}
                onClick={() => {
                  setActive(it.label);
                  setMobileOpen(false);
                }}
                className={cn(
                  "block rounded-[var(--radius-md)] px-4 py-3 text-[14px] transition-colors",
                  isActive
                    ? "bg-[var(--color-ink-50)] font-semibold text-[var(--color-ink-900)] dark:bg-white/[0.06]"
                    : "font-medium text-[var(--color-ink-700)] hover:bg-[var(--color-ink-50)]/70 dark:hover:bg-white/[0.04]",
                )}
              >
                {it.label}
              </a>
            );
          })}
          <div className="mt-1 border-t border-[var(--color-ink-100)]/70 dark:border-white/[0.06] pt-2">
            <Link
              href="/login"
              onClick={() => setMobileOpen(false)}
              className="block rounded-[var(--radius-md)] px-4 py-3 text-[14px] font-medium text-[var(--color-ink-700)] hover:bg-[var(--color-ink-50)]/70 dark:hover:bg-white/[0.04]"
            >
              Sign in
            </Link>
          </div>
        </motion.nav>
      )}
    </AnimatePresence>
    </>
  );
}
