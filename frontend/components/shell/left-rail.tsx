"use client";

import {
  AlertTriangle,
  Calendar,
  ChevronLeft,
  Database,
  Plus,
  Send,
  Share2,
  Smartphone,
  Star,
  Upload,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { useShell } from "@/components/shell/shell-context";
import { cn } from "@/lib/utils";

type RailAction = {
  icon: React.ReactNode;
  label: string;
  badge?: boolean;
};

const actions: RailAction[] = [
  { icon: <Share2 className="h-4 w-4" strokeWidth={2} />, label: "Share dispatch" },
  { icon: <Upload className="h-4 w-4" strokeWidth={2} />, label: "Export report" },
  { icon: <Star className="h-4 w-4" strokeWidth={2} />, label: "Saved routes" },
  { icon: <Plus className="h-4 w-4" strokeWidth={2} />, label: "New shipment" },
  { icon: <Smartphone className="h-4 w-4" strokeWidth={2} />, label: "Driver app" },
  { icon: <Database className="h-4 w-4" strokeWidth={2} />, label: "Records" },
  { icon: <Calendar className="h-4 w-4" strokeWidth={2} />, label: "Schedule" },
  { icon: <Send className="h-4 w-4" strokeWidth={2} />, label: "Broadcast" },
  { icon: <AlertTriangle className="h-4 w-4" strokeWidth={2} />, label: "Active alerts", badge: true },
];

const COLLAPSED_PX = 56;
const EXPANDED_PX = 232;
// Total horizontal space the rail consumes on the page (rail width + the
// fixed left-4 inset + an 8 px breathing buffer). The dashboard's <main>
// reads this from `--rail-width` to shift its left padding in lock-step.
const COLLAPSED_RAIL_LANE = `${COLLAPSED_PX + 16 + 8}px`; // 80 px
const EXPANDED_RAIL_LANE = `${EXPANDED_PX + 16 + 0}px`; //  248 px
const STORAGE_KEY = "tms-rail-expanded";

export function LeftRail() {
  // Rail expansion — persisted to localStorage and broadcast to <main>
  // via the `--rail-width` CSS variable on <html>. We default to false
  // for SSR (the markup matches), then hydrate the saved value on mount.
  const [expanded, setExpanded] = useState(false);
  const [hydrated, setHydrated] = useState(false);

  // Drawer state (tablet portrait) lives in ShellContext — TopNav's
  // hamburger toggles it. ESC closes; route changes auto-close.
  const { railDrawerOpen, closeRailDrawer } = useShell();

  useEffect(() => {
    const saved = localStorage.getItem(STORAGE_KEY) === "1";
    setExpanded(saved);
    document.documentElement.style.setProperty(
      "--rail-width",
      saved ? EXPANDED_RAIL_LANE : COLLAPSED_RAIL_LANE,
    );
    setHydrated(true);
  }, []);

  useEffect(() => {
    if (!hydrated) return;
    localStorage.setItem(STORAGE_KEY, expanded ? "1" : "0");
    document.documentElement.style.setProperty(
      "--rail-width",
      expanded ? EXPANDED_RAIL_LANE : COLLAPSED_RAIL_LANE,
    );
  }, [expanded, hydrated]);

  useEffect(() => {
    if (!railDrawerOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") closeRailDrawer();
    };
    window.addEventListener("keydown", onKey);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", onKey);
      document.body.style.overflow = prevOverflow;
    };
  }, [railDrawerOpen, closeRailDrawer]);

  return (
    <>
    <motion.aside
      initial={{ opacity: 0, x: -12 }}
      animate={{ opacity: 1, x: 0, width: expanded ? EXPANDED_PX : COLLAPSED_PX }}
      transition={{
        opacity: { duration: 0.55, delay: 0.15, ease: [0.22, 1, 0.36, 1] },
        x: { duration: 0.55, delay: 0.15, ease: [0.22, 1, 0.36, 1] },
        width: { type: "spring", stiffness: 220, damping: 28, mass: 0.6 },
      }}
      className="hidden lg:flex flex-col fixed left-4 top-20 bottom-4 z-30 pointer-events-none"
      aria-label="Quick actions"
    >
      {/* Action stack — centered in the available space between TopNav
          (cleared via top-20) and the theme toggles below. Using flex
          layout so the stack and the toggles can never overlap, even
          on short viewports — instead the stack shrinks/scrolls. */}
      <div className="flex-1 min-h-0 flex items-center justify-center">
        <ul className="pointer-events-auto flex flex-col gap-2 w-full">
          {/* Toggle is the FIRST button — its chevron rotates 180° when open. */}
          <PanelToggle expanded={expanded} onToggle={() => setExpanded((e) => !e)} />

          {actions.map((a, i) => (
            <motion.li
              key={a.label}
              initial={{ opacity: 0, y: -8, scale: 0.85 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              transition={{
                duration: 0.4,
                delay: 0.25 + i * 0.04,
                ease: [0.22, 1, 0.36, 1],
              }}
            >
              <RailButton label={a.label} badge={a.badge} expanded={expanded}>
                {a.icon}
              </RailButton>
            </motion.li>
          ))}
        </ul>
      </div>

    </motion.aside>

    {/* ── Tablet-portrait drawer ──────────────────────────────────── */}
    {/* Slides in from the left when the hamburger toggles it. Hidden  */}
    {/* on lg+ (the permanent rail takes over). Backdrop closes; ESC   */}
    {/* closes (see effect above).                                     */}
    <AnimatePresence>
      {railDrawerOpen && (
        <motion.div
          key="rail-drawer"
          className="lg:hidden fixed inset-0 z-50"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
        >
          {/* Backdrop */}
          <button
            type="button"
            aria-label="Close menu"
            onClick={closeRailDrawer}
            className="absolute inset-0 bg-[var(--color-ink-900)]/30 backdrop-blur-sm cursor-default"
          />

          {/* Panel */}
          <motion.aside
            initial={{ x: -320 }}
            animate={{ x: 0 }}
            exit={{ x: -320 }}
            transition={{ type: "spring", stiffness: 320, damping: 32, mass: 0.7 }}
            className="absolute left-0 inset-y-0 w-[280px] bg-white/95 dark:bg-[var(--color-ink-950)]/95 backdrop-blur-xl shadow-[0_20px_60px_-20px_rgba(15,23,42,0.35)] border-r border-[var(--color-ink-100)]/70 dark:border-white/[0.06] flex flex-col"
            aria-label="Quick actions"
          >
            {/* Drawer header — close button */}
            <div className="flex items-center justify-between px-4 py-4 border-b border-[var(--color-ink-100)]/70 dark:border-white/[0.06]">
              <span className="font-display text-[13px] font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-500)]">
                Quick actions
              </span>
              <button
                type="button"
                aria-label="Close menu"
                onClick={closeRailDrawer}
                className="grid h-9 w-9 place-items-center rounded-full text-[var(--color-ink-600)] hover:bg-[var(--color-ink-50)] dark:hover:bg-white/[0.06] cursor-pointer"
              >
                <X className="h-4 w-4" strokeWidth={2} />
              </button>
            </div>

            {/* Action list — always expanded in the drawer */}
            <ul className="flex-1 flex flex-col gap-1.5 px-3 py-4 overflow-y-auto">
              {actions.map((a) => (
                <li key={a.label} onClick={closeRailDrawer}>
                  <RailButton label={a.label} badge={a.badge} expanded>
                    {a.icon}
                  </RailButton>
                </li>
              ))}
            </ul>

          </motion.aside>
        </motion.div>
      )}
    </AnimatePresence>
    </>
  );
}

/* -------------------------------------------------------------------------- */
/* Panel toggle — chevron pill that flips 180° when the rail expands.         */
/* Visually it's identical to a RailButton so the toggle reads as part of the */
/* same stack, just with a chevron icon that animates.                        */
/* -------------------------------------------------------------------------- */
function PanelToggle({
  expanded,
  onToggle,
}: {
  expanded: boolean;
  onToggle: () => void;
}) {
  return (
    <li>
      <button
        aria-label={expanded ? "Collapse panel" : "Expand panel"}
        aria-expanded={expanded}
        onClick={onToggle}
        className={cn(
          "group relative flex h-10 items-center rounded-full bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:bg-white hover:text-[var(--color-ink-900)] cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_2px_6px_-2px_rgba(0,0,0,0.5)]",
          expanded ? "w-full justify-start gap-3 px-3" : "w-10 justify-center",
        )}
      >
        <motion.span
          animate={{ rotate: expanded ? 180 : 0 }}
          transition={{ type: "spring", stiffness: 320, damping: 22 }}
          className="flex h-4 w-4 items-center justify-center shrink-0"
        >
          <ChevronLeft className="h-4 w-4" strokeWidth={2} />
        </motion.span>
        {expanded && (
          <motion.span
            initial={{ opacity: 0, x: -4 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.25, delay: 0.1 }}
            className="text-[12.5px] font-medium tracking-tight whitespace-nowrap"
          >
            Collapse
          </motion.span>
        )}
      </button>
    </li>
  );
}

/* -------------------------------------------------------------------------- */
/* RailButton — circular when collapsed, full-width pill when expanded.       */
/* Tooltip only shows in collapsed state (label is in-place when expanded).   */
/* -------------------------------------------------------------------------- */
function RailButton({
  children,
  label,
  badge,
  expanded,
}: {
  children: React.ReactNode;
  label: string;
  badge?: boolean;
  expanded: boolean;
}) {
  return (
    <button
      aria-label={label}
      className={cn(
        "group relative flex h-10 items-center rounded-full bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:-translate-y-px hover:bg-white hover:text-[var(--color-ink-900)] hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.95),0_6px_14px_-4px_rgba(15,23,42,0.14)] cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_2px_6px_-2px_rgba(0,0,0,0.5)]",
        expanded ? "w-full justify-start gap-3 px-3" : "w-10 justify-center",
      )}
    >
      <span className="flex h-4 w-4 items-center justify-center shrink-0">{children}</span>

      {expanded ? (
        <motion.span
          initial={{ opacity: 0, x: -4 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.22, delay: 0.08 }}
          className="flex-1 min-w-0 text-left text-[12.5px] font-medium tracking-tight whitespace-nowrap truncate"
        >
          {label}
        </motion.span>
      ) : null}

      {badge && (
        <span
          className={cn(
            "absolute inline-flex",
            expanded ? "right-2 top-1/2 -translate-y-1/2" : "top-1 right-1",
          )}
        >
          <StatusPulse tone="coral" />
        </span>
      )}

      {/* Tooltip only when collapsed — when expanded the label is in-place. */}
      {!expanded && (
        <span
          role="tooltip"
          className="pointer-events-none absolute left-full ml-3 whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white opacity-0 translate-x-[-4px] transition-all duration-200 group-hover:opacity-100 group-hover:translate-x-0"
        >
          {label}
        </span>
      )}
    </button>
  );
}
