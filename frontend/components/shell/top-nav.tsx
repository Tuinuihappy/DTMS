"use client";

import { Bell, Compass, Menu, Search } from "lucide-react";
import { motion, useScroll, useTransform } from "motion/react";
import { useState } from "react";
import { Avatar } from "@/components/primitives/avatar";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { useShell } from "@/components/shell/shell-context";
import { cn } from "@/lib/utils";

const navItems = ["Home", "Messages", "Discover", "Wallet", "Projects"] as const;

export function TopNav() {
  const [active, setActive] = useState<(typeof navItems)[number]>("Home");
  const { toggleRailDrawer, railDrawerOpen } = useShell();

  // Stay pinned for the first 100 px of scroll, then slide up + fade
  // out so the dashboard content owns the rest of the viewport.
  const { scrollY } = useScroll();
  const opacity = useTransform(scrollY, [60, 100], [1, 0], { clamp: true });
  const driftY = useTransform(scrollY, [60, 100], [0, -24], { clamp: true });
  const pointerEvents = useTransform(scrollY, (v) => (v >= 100 ? "none" : "auto"));

  return (
    <motion.header
      initial={{ opacity: 0, y: -12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, ease: [0.22, 1, 0.36, 1] }}
      className="fixed top-4 inset-x-4 z-40 mx-auto max-w-[1440px]"
    >
      <motion.div
        style={{ opacity, y: driftY, pointerEvents }}
        className="relative flex items-center gap-2 px-2"
      >
        {/* Hamburger — tablet portrait only; opens the rail drawer */}
        <button
          type="button"
          aria-label={railDrawerOpen ? "Close menu" : "Open menu"}
          aria-expanded={railDrawerOpen}
          onClick={toggleRailDrawer}
          className="grid lg:hidden h-10 w-10 place-items-center rounded-full bg-white/70 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.8),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:bg-white hover:text-[var(--color-ink-900)] cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
        >
          <Menu className="h-[18px] w-[18px]" strokeWidth={2} />
        </button>

        {/* Logo + wordmark */}
        <a
          href="#"
          className="flex items-center gap-3 pl-1 pr-4 py-1.5 rounded-full transition-colors hover:bg-[var(--color-ink-50)]/60"
        >
          <span
            className="relative grid h-10 w-10 place-items-center rounded-full text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.45),0_6px_14px_-4px_rgba(255,107,91,0.55)]"
            style={{
              background:
                "radial-gradient(circle at 35% 30%, #FF8A78 0%, #FF6B5B 45%, #E8492F 100%)",
            }}
            aria-hidden
          >
            <span className="font-display text-[15px] font-semibold leading-none tracking-tight italic">
              t
            </span>
            <span className="absolute -top-0.5 -right-0.5 h-2 w-2 rounded-full bg-white shadow-[0_0_0_2px_rgba(255,107,91,0.25)]" />
          </span>
          <span className="font-display text-[1.05rem] font-semibold tracking-[0.04em] text-[var(--color-ink-900)] uppercase">
            TMS
          </span>
        </a>

        {/* Nav items — center */}
        <nav className="hidden md:flex items-center gap-1 ml-6 mr-auto">
          {navItems.map((item) => {
            const isActive = active === item;
            return (
              <button
                key={item}
                onClick={() => setActive(item)}
                className={cn(
                  "relative rounded-full px-4 py-2 text-[14px] transition-colors cursor-pointer",
                  isActive
                    ? "font-semibold text-[var(--color-ink-900)]"
                    : "font-medium text-[var(--color-ink-500)] hover:text-[var(--color-ink-800)]",
                )}
              >
                {item}
                {isActive && (
                  <motion.span
                    layoutId="top-nav-underline"
                    transition={{ type: "spring", stiffness: 380, damping: 30 }}
                    className="absolute left-3 right-3 -bottom-0.5 h-[2px] rounded-full bg-[var(--color-brand-500)]"
                  />
                )}
              </button>
            );
          })}
        </nav>

        {/* Search */}
        <div className="hidden lg:flex items-center gap-2 rounded-full bg-[var(--color-ink-50)]/80 pl-4 pr-1.5 py-1.5 w-[320px] transition-colors hover:bg-[var(--color-ink-50)] focus-within:bg-white focus-within:ring-2 focus-within:ring-[var(--color-brand-200)]">
          <input
            type="text"
            placeholder="Enter your search request..."
            className="flex-1 bg-transparent text-[13.5px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] outline-none"
            aria-label="Search"
          />
          <button
            aria-label="Search"
            className="grid h-8 w-8 place-items-center rounded-full text-[var(--color-ink-500)] hover:bg-white hover:text-[var(--color-ink-800)] cursor-pointer transition-colors"
          >
            <Search className="h-4 w-4" strokeWidth={2.2} />
          </button>
        </div>

        {/* Icon buttons */}
        <IconBtn label="Settings">
          <Compass className="h-[18px] w-[18px]" strokeWidth={2} />
        </IconBtn>
        <IconBtn label="Notifications" badge>
          <Bell className="h-[18px] w-[18px]" strokeWidth={2} />
        </IconBtn>

        {/* Avatar */}
        <a
          href="#"
          aria-label="Account"
          className="ml-1 rounded-full ring-2 ring-white shadow-[0_2px_8px_-2px_rgba(15,23,42,0.18)] hover:ring-[var(--color-brand-200)] transition-all cursor-pointer"
        >
          <Avatar name="Tuinui K." hue={32} size="md" />
        </a>
      </motion.div>
    </motion.header>
  );
}

function IconBtn({
  children,
  label,
  badge,
}: {
  children: React.ReactNode;
  label: string;
  badge?: boolean;
}) {
  return (
    <button
      aria-label={label}
      className="relative grid h-10 w-10 place-items-center rounded-full bg-white/70 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.8),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:bg-white hover:text-[var(--color-ink-900)] hover:-translate-y-px cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12]"
    >
      {children}
      {badge && (
        <span className="absolute top-2 right-2 inline-flex">
          <StatusPulse tone="coral" />
        </span>
      )}
    </button>
  );
}
