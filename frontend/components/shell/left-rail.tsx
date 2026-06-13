"use client";

import {
  Activity,
  AlertTriangle,
  BarChart3,
  Bot,
  Calendar,
  Gauge,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  CircuitBoard,
  ClipboardList,
  Database,
  DoorOpen,
  FileBarChart2,
  FileStack,
  Home,
  IdCard,
  LayoutDashboard,
  ListChecks,
  Map,
  RotateCcw,
  Send,
  Smartphone,
  Truck,
  Warehouse,
  Workflow,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { useShell } from "@/components/shell/shell-context";
import { cn } from "@/lib/utils";

type RailChild = {
  icon: React.ReactNode;
  label: string;
  href: string;
  hint?: string;
};

type RailAction = {
  icon: React.ReactNode;
  label: string;
  badge?: boolean;
  href?: string;
  children?: RailChild[];
};

const actions: RailAction[] = [
  { icon: <Home className="h-4 w-4" strokeWidth={2} />, label: "Home", href: "/home" },
  {
    icon: <LayoutDashboard className="h-4 w-4" strokeWidth={2} />,
    label: "Dashboard",
    children: [
      {
        icon: <Gauge className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Overview",
        href: "/dashboard",
        hint: "Live operations cockpit",
      },
      {
        icon: <BarChart3 className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Order analysis",
        href: "/dashboard/orders",
        hint: "Volume, SLA, dispatch funnel",
      },
      {
        icon: <Activity className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Robot analysis",
        href: "/dashboard/robots",
        hint: "Uptime, telemetry, alerts",
      },
    ],
  },
  {
    icon: <ClipboardList className="h-4 w-4" strokeWidth={2} />,
    label: "Delivery order",
    children: [
      {
        icon: <ListChecks className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Order list",
        href: "/delivery-orders/list",
        hint: "All active & past orders",
      },
      {
        icon: <RotateCcw className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Jobs queue",
        href: "/delivery-orders/jobs",
        hint: "Failed + stuck Planning Jobs",
      },
      {
        icon: <FileStack className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Order template",
        href: "/delivery-orders/order-templates",
        hint: "Reusable order recipes",
      },
      {
        icon: <Workflow className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Action template",
        href: "/delivery-orders/action-templates",
        hint: "Workflow building blocks",
      },
    ],
  },
  {
    icon: <Truck className="h-4 w-4" strokeWidth={2} />,
    label: "Fleet",
    children: [
      {
        icon: <Bot className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Robot",
        href: "/fleet/robots",
        hint: "Autonomous units",
      },
      {
        icon: <IdCard className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Driver",
        href: "/fleet/drivers",
        hint: "Roster & credentials",
      },
    ],
  },
  {
    icon: <Warehouse className="h-4 w-4" strokeWidth={2} />,
    label: "Facility",
    children: [
      {
        icon: <Map className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Map",
        href: "/facility/maps",
        hint: "Site layouts & zones",
      },
    ],
  },
  {
    icon: <CircuitBoard className="h-4 w-4" strokeWidth={2} />,
    label: "Equipment",
    children: [
      {
        icon: <DoorOpen className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Auto door",
        href: "/equipment/auto-doors",
        hint: "Door controllers & gates",
      },
    ],
  },
  {
    icon: <FileBarChart2 className="h-4 w-4" strokeWidth={2} />,
    label: "Reports",
    href: "/reports",
  },
  {
    icon: <Activity className="h-4 w-4" strokeWidth={2} />,
    label: "Projection health",
    href: "/admin/projections",
  },
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
              {a.children ? (
                <RailGroup action={a} expanded={expanded} />
              ) : (
                <RailButton label={a.label} badge={a.badge} expanded={expanded} href={a.href}>
                  {a.icon}
                </RailButton>
              )}
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
                <li key={a.label} onClick={a.children ? undefined : closeRailDrawer}>
                  {a.children ? (
                    <RailGroup action={a} expanded onChildNavigate={closeRailDrawer} />
                  ) : (
                    <RailButton label={a.label} badge={a.badge} expanded href={a.href}>
                      {a.icon}
                    </RailButton>
                  )}
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
  href,
}: {
  children: React.ReactNode;
  label: string;
  badge?: boolean;
  expanded: boolean;
  href?: string;
}) {
  const className = cn(
    "group relative flex h-10 items-center rounded-full bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:-translate-y-px hover:bg-white hover:text-[var(--color-ink-900)] hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.95),0_6px_14px_-4px_rgba(15,23,42,0.14)] cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_2px_6px_-2px_rgba(0,0,0,0.5)]",
    expanded ? "w-full justify-start gap-3 px-3" : "w-10 justify-center",
  );

  const inner = (
    <>
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
    </>
  );

  if (href) {
    return (
      <Link href={href} aria-label={label} className={className}>
        {inner}
      </Link>
    );
  }

  return (
    <button aria-label={label} className={className}>
      {inner}
    </button>
  );
}

/* -------------------------------------------------------------------------- */
/* RailGroup — multi-level entry point.                                       */
/* Collapsed rail → hovering the trigger opens a flyout panel anchored to the */
/* right, sized for 3 children with iconography + secondary hints.            */
/* Expanded rail  → clicking the trigger toggles an inline accordion that     */
/* reveals the children indented beneath the parent pill, with a chevron that */
/* rotates 180° and child rows that stagger in.                               */
/* Active child detection via usePathname → highlight + auto-expand on mount. */
/* -------------------------------------------------------------------------- */
function RailGroup({
  action,
  expanded,
  onChildNavigate,
}: {
  action: RailAction;
  expanded: boolean;
  onChildNavigate?: () => void;
}) {
  const children = action.children ?? [];
  const pathname = usePathname();
  const hasActiveChild = children.some((c) => pathname?.startsWith(c.href));

  // Inline accordion (expanded rail). Persisted across renders so toggling
  // the rail doesn't kill the user's intent. Auto-opens if the current
  // route is one of this group's children, so deep links feel resolved.
  const [accordionOpen, setAccordionOpen] = useState(hasActiveChild);
  useEffect(() => {
    if (hasActiveChild) setAccordionOpen(true);
  }, [hasActiveChild]);

  // Flyout (collapsed rail). Hover-driven but with a small grace timer so
  // moving the cursor from trigger → panel doesn't dismiss the panel.
  const [flyoutOpen, setFlyoutOpen] = useState(false);
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const openFlyout = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    setFlyoutOpen(true);
  };
  const closeFlyout = () => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    closeTimer.current = setTimeout(() => setFlyoutOpen(false), 140);
  };

  const triggerBase = cn(
    "group relative flex h-10 items-center rounded-full bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:-translate-y-px hover:bg-white hover:text-[var(--color-ink-900)] hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.95),0_6px_14px_-4px_rgba(15,23,42,0.14)] cursor-pointer dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_2px_6px_-2px_rgba(0,0,0,0.5)]",
    expanded ? "w-full justify-start gap-3 px-3" : "w-10 justify-center",
    // Active-route ring on the trigger (any child route active)
    hasActiveChild &&
      "ring-2 ring-[var(--color-brand-200)] dark:ring-[var(--color-brand-500)]/40 bg-white dark:bg-white/[0.1] text-[var(--color-ink-900)]",
  );

  return (
    <div
      className="relative"
      onMouseEnter={!expanded ? openFlyout : undefined}
      onMouseLeave={!expanded ? closeFlyout : undefined}
    >
      <button
        type="button"
        aria-label={action.label}
        aria-haspopup="menu"
        aria-expanded={expanded ? accordionOpen : flyoutOpen}
        onClick={expanded ? () => setAccordionOpen((v) => !v) : undefined}
        className={triggerBase}
      >
        <span className="flex h-4 w-4 items-center justify-center shrink-0">
          {action.icon}
        </span>

        {expanded && (
          <>
            <motion.span
              initial={{ opacity: 0, x: -4 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.22, delay: 0.08 }}
              className="flex-1 min-w-0 text-left text-[12.5px] font-medium tracking-tight whitespace-nowrap truncate"
            >
              {action.label}
            </motion.span>
            <motion.span
              animate={{ rotate: accordionOpen ? 180 : 0 }}
              transition={{ type: "spring", stiffness: 380, damping: 24 }}
              className="flex h-3.5 w-3.5 items-center justify-center text-[var(--color-ink-500)] shrink-0"
            >
              <ChevronDown className="h-3.5 w-3.5" strokeWidth={2.2} />
            </motion.span>
          </>
        )}

        {/* Active-child dot pip — sits at top-right when collapsed so the
            user knows this group contains the current page even when the
            label/chevron aren't visible. */}
        {!expanded && hasActiveChild && (
          <span className="absolute top-0.5 right-0.5 h-1.5 w-1.5 rounded-full bg-[var(--color-brand-500)] ring-2 ring-white dark:ring-[var(--color-canvas)]" />
        )}

        {/* Tooltip when collapsed — replaced by flyout on hover, but stays
            as a fallback for keyboard-focus / no-hover devices. */}
        {!expanded && !flyoutOpen && (
          <span
            role="tooltip"
            className="pointer-events-none absolute left-full ml-3 whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white opacity-0 translate-x-[-4px] transition-all duration-200 group-hover:opacity-100 group-hover:translate-x-0"
          >
            {action.label}
          </span>
        )}
      </button>

      {/* ── Inline accordion (expanded rail) ─────────────────────────── */}
      {expanded && (
        <AnimatePresence initial={false}>
          {accordionOpen && (
            <motion.ul
              key="acc"
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: "auto", opacity: 1 }}
              exit={{ height: 0, opacity: 0 }}
              transition={{
                height: { duration: 0.28, ease: [0.22, 1, 0.36, 1] },
                opacity: { duration: 0.2 },
              }}
              className="relative overflow-hidden pl-3 mt-1"
            >
              {/* Vertical thread connecting children to parent — anchored to
                  the icon column so it reads as a structured tree. */}
              <span
                aria-hidden
                className="absolute left-[16px] top-1 bottom-1 w-px bg-gradient-to-b from-[var(--color-ink-200)] via-[var(--color-ink-100)] to-transparent dark:from-white/[0.12] dark:via-white/[0.06]"
              />
              {children.map((c, i) => {
                const active = pathname?.startsWith(c.href);
                return (
                  <motion.li
                    key={c.href}
                    initial={{ opacity: 0, x: -6 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{
                      duration: 0.28,
                      delay: 0.04 + i * 0.05,
                      ease: [0.22, 1, 0.36, 1],
                    }}
                    className="py-0.5"
                  >
                    <Link
                      href={c.href}
                      onClick={onChildNavigate}
                      className={cn(
                        "relative flex h-8 items-center gap-2.5 rounded-full pl-3 pr-3 text-[12px] font-medium transition-colors",
                        active
                          ? "bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_6px_14px_-6px_rgba(14,21,48,0.5)] dark:bg-[var(--color-brand-500)] dark:text-white"
                          : "text-[var(--color-ink-600)] hover:bg-white/80 hover:text-[var(--color-ink-900)] dark:hover:bg-white/[0.06]",
                      )}
                    >
                      {/* Branch tick into the vertical thread */}
                      <span
                        aria-hidden
                        className={cn(
                          "absolute left-[1px] top-1/2 h-px w-2 -translate-y-1/2",
                          active
                            ? "bg-[var(--color-brand-500)]"
                            : "bg-[var(--color-ink-200)] dark:bg-white/[0.12]",
                        )}
                      />
                      <span className="flex h-3.5 w-3.5 items-center justify-center shrink-0">
                        {c.icon}
                      </span>
                      <span className="truncate">{c.label}</span>
                    </Link>
                  </motion.li>
                );
              })}
            </motion.ul>
          )}
        </AnimatePresence>
      )}

      {/* ── Flyout panel (collapsed rail) ────────────────────────────── */}
      {!expanded && (
        <AnimatePresence>
          {flyoutOpen && (
            <motion.div
              key="fly"
              role="menu"
              initial={{ opacity: 0, x: -8, scale: 0.97 }}
              animate={{ opacity: 1, x: 0, scale: 1 }}
              exit={{ opacity: 0, x: -6, scale: 0.97 }}
              transition={{ duration: 0.2, ease: [0.22, 1, 0.36, 1] }}
              onMouseEnter={openFlyout}
              onMouseLeave={closeFlyout}
              className="absolute left-full top-1/2 -translate-y-1/2 ml-3 z-40 w-[252px] rounded-[20px] glass-strong p-2.5 pointer-events-auto"
            >
              {/* Aurora bleed inside the panel — bright coral wedge top-left,
                  brand-blue wedge bottom-right. Subtle, mostly washed out. */}
              <span
                aria-hidden
                className="pointer-events-none absolute inset-0 rounded-[20px] overflow-hidden"
              >
                <span
                  className="absolute -top-6 -left-6 h-24 w-24 rounded-full opacity-60 blur-2xl"
                  style={{
                    background:
                      "radial-gradient(circle at 40% 40%, rgba(255,138,120,0.55), transparent 60%)",
                  }}
                />
                <span
                  className="absolute -bottom-8 -right-6 h-28 w-28 rounded-full opacity-50 blur-2xl"
                  style={{
                    background:
                      "radial-gradient(circle at 60% 60%, rgba(143,156,255,0.55), transparent 60%)",
                  }}
                />
              </span>

              {/* Header */}
              <div className="relative flex items-center gap-2 px-2.5 pb-2 pt-1">
                <span className="grid h-7 w-7 place-items-center rounded-lg bg-gradient-to-br from-[var(--color-pastel-peach)] to-[#fcb98a] text-[var(--color-pastel-peach-ink)] shadow-[inset_0_1px_0_rgba(255,255,255,0.6)]">
                  {action.icon}
                </span>
                <div className="min-w-0">
                  <div className="text-[9.5px] font-bold uppercase tracking-[0.16em] text-[var(--color-ink-400)]">
                    Section
                  </div>
                  <div className="text-[12.5px] font-semibold tracking-tight text-[var(--color-ink-900)] truncate">
                    {action.label}
                  </div>
                </div>
              </div>

              <div className="relative inset-divider my-1" />

              {/* Children list */}
              <ul className="relative flex flex-col gap-0.5 mt-1">
                {children.map((c, i) => {
                  const active = pathname?.startsWith(c.href);
                  return (
                    <motion.li
                      key={c.href}
                      initial={{ opacity: 0, x: -6 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{
                        duration: 0.28,
                        delay: 0.04 + i * 0.05,
                        ease: [0.22, 1, 0.36, 1],
                      }}
                    >
                      <Link
                        href={c.href}
                        onClick={() => {
                          setFlyoutOpen(false);
                          onChildNavigate?.();
                        }}
                        className={cn(
                          "group/flyitem relative flex items-center gap-2.5 rounded-[14px] px-2 py-2 transition-colors",
                          active
                            ? "bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_8px_18px_-8px_rgba(14,21,48,0.6)] dark:bg-[var(--color-brand-500)]"
                            : "text-[var(--color-ink-800)] hover:bg-white/80 dark:hover:bg-white/[0.08]",
                        )}
                      >
                        <span
                          className={cn(
                            "grid h-7 w-7 place-items-center rounded-lg shrink-0 transition-colors",
                            active
                              ? "bg-white/15 text-white"
                              : "bg-[var(--color-ink-50)] text-[var(--color-ink-700)] dark:bg-white/[0.06] dark:text-[var(--color-ink-700)]",
                          )}
                        >
                          {c.icon}
                        </span>
                        <span className="min-w-0 flex-1">
                          <span className="block text-[12.5px] font-semibold tracking-tight truncate">
                            {c.label}
                          </span>
                          {c.hint && (
                            <span
                              className={cn(
                                "block text-[10.5px] truncate transition-colors",
                                active ? "text-white/70" : "text-[var(--color-ink-500)]",
                              )}
                            >
                              {c.hint}
                            </span>
                          )}
                        </span>
                        <ChevronRight
                          className={cn(
                            "h-3.5 w-3.5 shrink-0 transition-all",
                            active
                              ? "text-white"
                              : "text-[var(--color-ink-300)] -translate-x-0.5 opacity-0 group-hover/flyitem:opacity-100 group-hover/flyitem:translate-x-0",
                          )}
                          strokeWidth={2.4}
                        />
                      </Link>
                    </motion.li>
                  );
                })}
              </ul>
            </motion.div>
          )}
        </AnimatePresence>
      )}
    </div>
  );
}
