"use client";

import {
  Activity,
  AlertTriangle,
  BarChart3,
  Bot,
  Gauge,
  ChevronLeft,
  ChevronRight,
  CircuitBoard,
  ClipboardList,
  DoorOpen,
  FileBarChart2,
  FileStack,
  Home,
  IdCard,
  KeyRound,
  Layers,
  LayoutDashboard,
  ListChecks,
  Map,
  Package,
  Route,
  RotateCcw,
  ScrollText,
  Server,
  ShieldCheck,
  Truck,
  Users,
  Warehouse,
  Workflow,
  X,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { ModalLayer } from "@/components/primitives/modal-layer";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { useShell } from "@/components/shell/shell-context";
import { useAuth } from "@/components/auth/auth-provider";
import { Permissions } from "@/lib/auth/permissions";
import { spring, T } from "@/lib/motion";
import { useDismissableLayer } from "@/lib/overlay/use-layer";
import {
  RAIL_COLLAPSED_LANE,
  RAIL_COLLAPSED_PX,
  RAIL_COOKIE,
  RAIL_EXPANDED_LANE,
  RAIL_EXPANDED_PX,
} from "@/lib/shell";
import { cn } from "@/lib/utils";

type RailChild = {
  icon: React.ReactNode;
  label: string;
  href: string;
  hint?: string;
  // Phase S.6 — when set, the child is rendered only if the current
  // principal holds the permission (wildcard-matched). Items without
  // `requires` are visible to everyone.
  requires?: string;
  // Page not built yet: shown dimmed with a "Soon" tag, not a link (the
  // href would 404). Drop the flag once the route exists.
  soon?: boolean;
};

type RailAction = {
  icon: React.ReactNode;
  label: string;
  badge?: boolean;
  href?: string;
  soon?: boolean;
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
        icon: <Package className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Items",
        href: "/delivery-orders/items",
        hint: "Search items across orders",
        requires: Permissions.DeliveryOrder.ItemRead,
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
    icon: <Route className="h-4 w-4" strokeWidth={2} />,
    label: "Dispatch",
    children: [
      {
        icon: <ListChecks className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Trip list",
        href: "/dispatch/trips",
        hint: "Envelope dispatches across every order",
      },
      {
        icon: <Users className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Manual operators",
        href: "/dispatch/manual",
        hint: "Who carries what, reassign, overrides",
        requires: Permissions.Operator.PoolRead,
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
        soon: true,
      },
      {
        icon: <IdCard className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Driver",
        href: "/fleet/drivers",
        hint: "Roster & credentials",
        soon: true,
      },
      {
        icon: <Package className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Carrier",
        href: "/fleet/carriers",
        hint: "Racks & carts",
        requires: Permissions.Fleet.CarrierRead,
      },
      {
        icon: <Layers className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Carrier type",
        href: "/fleet/carrier-types",
        hint: "Kinds of rack & cart",
        requires: Permissions.Fleet.CarrierTypeRead,
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
        soon: true,
      },
    ],
  },
  {
    icon: <FileBarChart2 className="h-4 w-4" strokeWidth={2} />,
    label: "Reports",
    href: "/reports",
  },
  {
    icon: <ShieldCheck className="h-4 w-4" strokeWidth={2} />,
    label: "Access control",
    children: [
      {
        icon: <KeyRound className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Permissions",
        href: "/admin/permissions",
        hint: "Catalog of dtms:* perms",
      },
      {
        icon: <ScrollText className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Audit log",
        href: "/admin/audit-log",
        hint: "Who changed what, when",
      },
      {
        // Phase S.6 — federated source-system admin (SystemClient +
        // SystemEventSubscription CRUD). Gated behind iam:system:read so
        // operators without admin-on-systems don't see the entry.
        icon: <Server className="h-3.5 w-3.5" strokeWidth={2.1} />,
        label: "Systems",
        href: "/admin/systems",
        hint: "Source systems & callbacks",
        requires: Permissions.Iam.SystemRead,
      },
    ],
  },
  {
    icon: <Activity className="h-4 w-4" strokeWidth={2} />,
    label: "Projection health",
    href: "/admin/projections",
  },
  {
    icon: <Gauge className="h-4 w-4" strokeWidth={2} />,
    label: "System status",
    href: "/admin/system-status",
  },
  { icon: <AlertTriangle className="h-4 w-4" strokeWidth={2} />, label: "Active alerts", soon: true },
];

// Sections, by what the operator is doing. Items are looked up by label in
// `actions` above, so the menu data stays in one list. ADMIN is pinned to
// the bottom of the panel (outside the scroll area): most operators never
// use it, and it shouldn't push the daily pages off screen.
type RailSection = { label?: string; items: RailAction[] };

const byLabel = (labels: string[]): RailAction[] =>
  labels.map((l) => {
    const a = actions.find((x) => x.label === l);
    if (!a) throw new Error(`left-rail: no action "${l}"`);
    return a;
  });

const SECTIONS: RailSection[] = [
  { items: byLabel(["Home", "Dashboard"]) },
  { label: "Operations", items: byLabel(["Delivery order", "Dispatch"]) },
  { label: "Assets", items: byLabel(["Fleet", "Facility", "Equipment"]) },
  { label: "Insights", items: byLabel(["Reports"]) },
];
const FOOTER: RailSection = {
  label: "Admin",
  items: byLabel(["Access control", "Projection health", "System status", "Active alerts"]),
};

const COLLAPSED_PX = RAIL_COLLAPSED_PX;
const EXPANDED_PX = RAIL_EXPANDED_PX;
// Pre-cookie versions kept the state in localStorage under the same name.
const LEGACY_STORAGE_KEY = RAIL_COOKIE;

/** Exact match, or a sub-path — but a sibling with a longer matching href
 *  wins, so /dashboard/orders lights "Order analysis", not "Overview". */
function isActiveHref(pathname: string | null, href: string, siblings: { href: string }[] = []): boolean {
  if (!pathname) return false;
  const matches = (h: string) => pathname === h || pathname.startsWith(`${h}/`);
  if (!matches(href)) return false;
  return !siblings.some((s) => s.href !== href && s.href.length > href.length && matches(s.href));
}

export function LeftRail({ initialExpanded = false }: { initialExpanded?: boolean }) {
  // Rail expansion — persisted to a cookie (app/(app)/layout.tsx reads it
  // on the server, so the first render is already the right width) and
  // broadcast to <main> via the `--rail-width` CSS variable on <html>.
  const [expanded, setExpanded] = useState(initialExpanded);

  // Drawer state (tablet portrait) lives in ShellContext — TopNav's
  // hamburger toggles it. ESC closes; route changes auto-close.
  const { railDrawerOpen, closeRailDrawer } = useShell();

  // One-time carry-over from the localStorage era: someone who had the
  // rail open keeps it open after this change.
  useEffect(() => {
    try {
      if (!document.cookie.includes(`${RAIL_COOKIE}=`) && localStorage.getItem(LEGACY_STORAGE_KEY) === "1") {
        setExpanded(true);
      }
      localStorage.removeItem(LEGACY_STORAGE_KEY);
    } catch {
      // Storage blocked — the cookie alone is fine.
    }
  }, []);

  useEffect(() => {
    document.cookie = `${RAIL_COOKIE}=${expanded ? "1" : "0"}; path=/; max-age=31536000; samesite=lax`;
    document.documentElement.style.setProperty(
      "--rail-width",
      expanded ? RAIL_EXPANDED_LANE : RAIL_COLLAPSED_LANE,
    );
  }, [expanded]);

  // Lock body scroll while the drawer is open. Esc is ModalLayer's.
  useEffect(() => {
    if (!railDrawerOpen) return;
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prevOverflow;
    };
  }, [railDrawerOpen]);

  return (
    <>
    <motion.aside
      // The shell is persistent (app/(app)/layout.tsx), so this entrance
      // plays once per full load, not per navigation. Width starts at its
      // real value — no spring from collapsed on first paint.
      initial={{ opacity: 0, x: -8, width: expanded ? EXPANDED_PX : COLLAPSED_PX }}
      animate={{ opacity: 1, x: 0, width: expanded ? EXPANDED_PX : COLLAPSED_PX }}
      transition={{
        opacity: T.base,
        x: T.base,
        width: spring.gentle,
      }}
      // Top-aligned and scrollable. The stack used to be centred
      // vertically with no scroll: once it outgrew the viewport it
      // overflowed both ways, up under the TopNav logo and off the bottom.
      className="hidden lg:flex flex-col fixed left-4 top-20 bottom-4 z-30"
      aria-label="Main navigation"
    >
      {/* Collapse toggle — first pill, outside the scroll area. */}
      <div className={cn("shrink-0 px-2 pb-1", !expanded && "flex justify-center")}>
        <button
          type="button"
          aria-label={expanded ? "Collapse panel" : "Expand panel"}
          aria-expanded={expanded}
          onClick={() => setExpanded((e) => !e)}
          className={cn(rowBase, rowShape(expanded), rowIdle)}
        >
          <motion.span
            animate={{ rotate: expanded ? 0 : 180 }}
            transition={spring.snappy}
            className="flex h-4 w-4 shrink-0 items-center justify-center"
          >
            <ChevronLeft className="h-4 w-4" strokeWidth={2} />
          </motion.span>
          {expanded && <span className="whitespace-nowrap">Collapse</span>}
        </button>
      </div>

      {/* Scroll area — soft fade at both ends says there's more. */}
      <nav
        className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden px-2 py-2 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden [mask-image:linear-gradient(to_bottom,transparent,black_10px,black_calc(100%_-_10px),transparent)]"
      >
        <RailNav sections={SECTIONS} expanded={expanded} scope="rail" />
      </nav>

      <div className="shrink-0 px-2 pt-1">
        <RailNav sections={[FOOTER]} expanded={expanded} scope="rail" />
      </div>
    </motion.aside>

    {/* ── Tablet-portrait drawer ──────────────────────────────────── */}
    {/* Slides in from the left when the hamburger toggles it. Hidden  */}
    {/* on lg+ (the permanent rail takes over). Backdrop and Esc close */}
    {/* it (ModalLayer); the effect above locks body scroll.          */}
    <ModalLayer
      open={railDrawerOpen}
      onClose={closeRailDrawer}
      initialFocus="panel"
      label="Main navigation"
      backdropClassName="lg:hidden bg-[var(--color-ink-900)]/30"
    >
      {({ panelProps, panelStyle }) => (
          <motion.aside
            {...panelProps}
            style={panelStyle}
            key="rail-drawer"
            initial={{ x: -320 }}
            animate={{ x: 0 }}
            exit={{ x: -320, transition: T.exit }}
            transition={spring.drawer}
            className="lg:hidden fixed left-0 inset-y-0 w-[280px] bg-[var(--color-surface)] shadow-[0_20px_60px_-20px_rgba(15,23,42,0.35)] border-r border-[var(--color-ink-100)]/70 dark:border-white/[0.06] flex flex-col outline-none"
          >
            <div className="flex h-14 shrink-0 items-center justify-between pl-5 pr-3 border-b border-[var(--color-ink-100)]/70 dark:border-white/[0.06]">
              <span className="text-[10px] font-semibold uppercase tracking-[0.16em] text-[var(--color-ink-400)]">
                Menu
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

            {/* Always expanded in the drawer; same sections as the rail. */}
            <nav className="min-h-0 flex-1 overflow-y-auto px-3 py-2">
              <RailNav sections={SECTIONS} expanded scope="drawer" onNavigate={closeRailDrawer} />
            </nav>
            <div className="shrink-0 border-t border-[var(--color-ink-100)]/70 px-3 pb-3 pt-1 dark:border-white/[0.06]">
              <RailNav sections={[FOOTER]} expanded scope="drawer" onNavigate={closeRailDrawer} />
            </div>
          </motion.aside>
      )}
    </ModalLayer>
    </>
  );
}

/* -------------------------------------------------------------------------- */
/* RailNav — sections of rows. Expanded: small caps label over each section.  */
/* Collapsed: a short hairline between sections instead.                      */
/* -------------------------------------------------------------------------- */
function RailNav({
  sections,
  expanded,
  scope,
  onNavigate,
}: {
  sections: RailSection[];
  expanded: boolean;
  /** layoutId namespace — the rail and the drawer each have their own
   *  sliding "active" pill. */
  scope: string;
  onNavigate?: () => void;
}) {
  return (
    <>
      {sections.map((s, si) => (
        <div key={s.label ?? `s${si}`}>
          {s.label &&
            (expanded ? (
              <div className="px-2.5 pb-1 pt-3 text-[10px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)] whitespace-nowrap">
                {s.label}
              </div>
            ) : (
              <div aria-hidden className="mx-auto my-2 h-px w-6 bg-[var(--color-ink-900)]/[0.08] dark:bg-white/[0.08]" />
            ))}
          <ul className={cn("flex flex-col gap-2", !expanded && "items-center")}>
            {s.items.map((a) => (
              <li key={a.label}>
                {a.children ? (
                  <RailGroup action={a} expanded={expanded} scope={scope} onChildNavigate={onNavigate} />
                ) : (
                  <RailButton action={a} expanded={expanded} scope={scope} onNavigate={onNavigate} />
                )}
              </li>
            ))}
          </ul>
        </div>
      ))}
    </>
  );
}

// Every row is its own white pill (the rail's look — kept on purpose).
const rowBase =
  "relative flex items-center rounded-full text-[12.5px] font-medium tracking-tight transition-[background-color,box-shadow,color] duration-200 cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-brand-500)]";
const rowShape = (expanded: boolean) => (expanded ? "h-10 w-full gap-3 px-3" : "h-10 w-10 justify-center");
const rowIdle =
  "bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] hover:bg-white hover:text-[var(--color-ink-900)] hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.95),0_6px_14px_-4px_rgba(15,23,42,0.14)] dark:bg-white/[0.06] dark:hover:bg-white/[0.12] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_2px_6px_-2px_rgba(0,0,0,0.5)]";

/** The one filled thing in the panel. Shared layoutId per scope, so it
 *  slides from the old page's row to the new one. */
function ActivePill({ scope, rounded = "rounded-full" }: { scope: string; rounded?: string }) {
  return (
    <motion.span
      layoutId={`${scope}-active`}
      transition={spring.snappy}
      aria-hidden
      className={cn(
        "absolute inset-0 bg-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_6px_14px_-6px_rgba(14,21,48,0.5)] dark:bg-[var(--color-brand-500)]",
        rounded,
      )}
    />
  );
}

/* -------------------------------------------------------------------------- */
/* Floating layer for the collapsed rail — tooltips and group flyouts render  */
/* in a body portal at a fixed position next to their row. Inside the rail    */
/* they'd be clipped by the scroll container.                                 */
/* -------------------------------------------------------------------------- */
function useMounted() {
  const [m, setM] = useState(false);
  useEffect(() => setM(true), []);
  return m;
}

function FloatBeside({
  anchor,
  open,
  align = "center",
  children,
}: {
  anchor: React.RefObject<HTMLElement | null>;
  open: boolean;
  align?: "center" | "top";
  children: React.ReactNode;
}) {
  const mounted = useMounted();
  const [pos, setPos] = useState<{ left: number; top: number } | null>(null);
  useLayoutEffect(() => {
    if (!open || !anchor.current) return;
    const r = anchor.current.getBoundingClientRect();
    setPos({
      left: r.right + 12,
      top:
        align === "center"
          ? r.top + r.height / 2
          : // Keep a tall flyout on screen when its row is low in the rail.
            Math.max(12, Math.min(r.top - 8, window.innerHeight - 340)),
    });
  }, [open, anchor, align]);

  if (!mounted) return null;
  return createPortal(
    <AnimatePresence>
      {open && pos && (
        <motion.div
          key="float"
          style={{ position: "fixed", left: pos.left, top: pos.top, zIndex: 60 }}
          initial={{ opacity: 0, x: -6, y: align === "center" ? "-50%" : 0 }}
          animate={{ opacity: 1, x: 0, y: align === "center" ? "-50%" : 0, transition: T.fast }}
          exit={{ opacity: 0, x: -4, transition: T.exit }}
        >
          {children}
        </motion.div>
      )}
    </AnimatePresence>,
    document.body,
  );
}

function Tooltip({ children }: { children: React.ReactNode }) {
  return (
    <span
      role="tooltip"
      className="pointer-events-none block whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white shadow-lg"
    >
      {children}
    </span>
  );
}

/* -------------------------------------------------------------------------- */
/* RailButton — a top-level page.                                             */
/* -------------------------------------------------------------------------- */
function RailButton({
  action,
  expanded,
  scope,
  onNavigate,
}: {
  action: RailAction;
  expanded: boolean;
  scope: string;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  const ref = useRef<HTMLElement | null>(null);
  const [hover, setHover] = useState(false);
  const tipProps = expanded
    ? {}
    : {
        onMouseEnter: () => setHover(true),
        onMouseLeave: () => setHover(false),
        onFocus: () => setHover(true),
        onBlur: () => setHover(false),
      };

  if (action.soon) {
    // Not built yet — dimmed and inert. aria-disabled (not disabled) so it
    // stays focusable and the tooltip still says what it is.
    return (
      <>
        <span
          ref={ref}
          role="button"
          aria-disabled="true"
          tabIndex={0}
          aria-label={`${action.label} (coming soon)`}
          className={cn(rowBase, rowShape(expanded), "cursor-not-allowed border border-dashed border-[var(--color-ink-200)] text-[var(--color-ink-400)] dark:border-white/[0.12]")}
          {...tipProps}
        >
          <span className="flex h-4 w-4 shrink-0 items-center justify-center opacity-60">{action.icon}</span>
          {expanded && (
            <>
              <span className="min-w-0 flex-1 truncate whitespace-nowrap text-left">{action.label}</span>
              <SoonTag />
            </>
          )}
        </span>
        {!expanded && (
          <FloatBeside anchor={ref} open={hover}>
            <Tooltip>{action.label} · Soon</Tooltip>
          </FloatBeside>
        )}
      </>
    );
  }

  const active = !!action.href && isActiveHref(pathname, action.href);
  const inner = (
    <>
      {active && <ActivePill scope={scope} />}
      <span className="relative flex h-4 w-4 shrink-0 items-center justify-center">{action.icon}</span>
      {expanded && <span className="relative min-w-0 flex-1 truncate whitespace-nowrap text-left">{action.label}</span>}
      {action.badge && (
        <span className={cn("absolute inline-flex", expanded ? "right-2.5 top-1/2 -translate-y-1/2" : "right-1 top-1")}>
          <StatusPulse tone="coral" />
        </span>
      )}
    </>
  );
  const className = cn(rowBase, rowShape(expanded), active ? "border border-transparent text-white" : rowIdle);

  return (
    <>
      {action.href ? (
        <Link
          ref={ref as React.RefObject<HTMLAnchorElement>}
          href={action.href}
          aria-label={expanded ? undefined : action.label}
          aria-current={active ? "page" : undefined}
          onClick={onNavigate}
          className={className}
          {...tipProps}
        >
          {inner}
        </Link>
      ) : (
        <button
          ref={ref as React.RefObject<HTMLButtonElement>}
          type="button"
          aria-label={action.label}
          className={className}
          {...tipProps}
        >
          {inner}
        </button>
      )}
      {!expanded && (
        <FloatBeside anchor={ref} open={hover}>
          <Tooltip>{action.label}</Tooltip>
        </FloatBeside>
      )}
    </>
  );
}

function SoonTag() {
  return (
    <span className="shrink-0 rounded-full bg-[var(--color-ink-100)] px-1.5 py-px text-[9.5px] font-semibold uppercase tracking-[0.08em] text-[var(--color-ink-500)] dark:bg-white/[0.08]">
      Soon
    </span>
  );
}

/* -------------------------------------------------------------------------- */
/* RailGroup — a section with children.                                       */
/* Expanded rail → click toggles an inline accordion; children hang off a     */
/* thin thread under the icon column.                                         */
/* Collapsed rail → hover (or click / focus) opens a flyout beside the row.   */
/* The active pill sits on the child when it's visible, otherwise on the      */
/* group row — so closing the accordion slides it up onto the parent.         */
/* -------------------------------------------------------------------------- */
function RailGroup({
  action,
  expanded,
  scope,
  onChildNavigate,
}: {
  action: RailAction;
  expanded: boolean;
  scope: string;
  onChildNavigate?: () => void;
}) {
  const { hasPermission, permissions } = useAuth();
  // Phase S.6 — filter children that declare `requires`. While
  // permissions are still loading (null), HIDE gated children to avoid
  // flicker (they appear once the fetch resolves). Ungated children
  // remain visible throughout.
  const allChildren = action.children ?? [];
  const children = allChildren.filter(
    (c) => !c.requires || (permissions !== null && hasPermission(c.requires)),
  );
  // If every child is gated out, the whole group is invisible.
  const hidden = allChildren.length > 0 && children.length === 0;
  const pathname = usePathname();
  const live = children.filter((c) => !c.soon);
  const activeChild = live.find((c) => isActiveHref(pathname, c.href, live));
  const hasActiveChild = !!activeChild;

  // Inline accordion (expanded rail). Auto-opens when the current route is
  // one of this group's children, so deep links feel resolved.
  const [accordionOpen, setAccordionOpen] = useState(hasActiveChild);
  useEffect(() => {
    if (hasActiveChild) setAccordionOpen(true);
  }, [hasActiveChild]);

  // Flyout (collapsed rail). Hover-driven with a grace timer so moving the
  // cursor from row → flyout doesn't dismiss it.
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const [flyoutOpen, setFlyoutOpen] = useState(false);
  const closeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const openFlyout = useCallback(() => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    setFlyoutOpen(true);
  }, []);
  const closeFlyout = useCallback(() => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
    closeTimer.current = setTimeout(() => setFlyoutOpen(false), 140);
  }, []);
  useEffect(() => () => {
    if (closeTimer.current) clearTimeout(closeTimer.current);
  }, []);
  // Esc closes it (layer stack), as does any navigation or scroll — it is
  // fixed-positioned, so a scrolled rail would leave it pointing nowhere.
  useDismissableLayer({ open: flyoutOpen, onDismiss: () => setFlyoutOpen(false) });
  useEffect(() => setFlyoutOpen(false), [pathname, expanded]);
  useEffect(() => {
    if (!flyoutOpen) return;
    const onScroll = () => setFlyoutOpen(false);
    window.addEventListener("scroll", onScroll, true);
    return () => window.removeEventListener("scroll", onScroll, true);
  }, [flyoutOpen]);

  if (hidden) return null;

  // Pill on the parent row whenever the active child itself isn't on screen.
  const parentCarriesPill = hasActiveChild && (!expanded || !accordionOpen);

  return (
    <div
      className="relative"
      onMouseEnter={!expanded ? openFlyout : undefined}
      onMouseLeave={!expanded ? closeFlyout : undefined}
    >
      <button
        ref={triggerRef}
        type="button"
        aria-label={expanded ? undefined : action.label}
        aria-haspopup={expanded ? undefined : "menu"}
        aria-expanded={expanded ? accordionOpen : flyoutOpen}
        onClick={expanded ? () => setAccordionOpen((v) => !v) : () => setFlyoutOpen((v) => !v)}
        className={cn(
          rowBase,
          rowShape(expanded),
          parentCarriesPill
            ? "border border-transparent text-white"
            : hasActiveChild
              ? cn(rowIdle, "ring-2 ring-[var(--color-brand-200)] text-[var(--color-ink-900)] dark:ring-[var(--color-brand-500)]/40")
              : rowIdle,
        )}
      >
        {parentCarriesPill && <ActivePill scope={scope} />}
        <span
          className={cn(
            "relative flex h-4 w-4 shrink-0 items-center justify-center",
            hasActiveChild && !parentCarriesPill && "text-[var(--color-brand-500)]",
          )}
        >
          {action.icon}
        </span>

        {expanded && (
          <>
            <span className="relative min-w-0 flex-1 truncate whitespace-nowrap text-left">{action.label}</span>
            <motion.span
              animate={{ rotate: accordionOpen ? 90 : 0 }}
              transition={spring.snappy}
              className={cn(
                "relative flex h-3.5 w-3.5 shrink-0 items-center justify-center",
                parentCarriesPill ? "text-white/70" : "text-[var(--color-ink-400)]",
              )}
            >
              <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.2} />
            </motion.span>
          </>
        )}
      </button>

      {/* ── Inline accordion (expanded rail) ─────────────────────────── */}
      {expanded && (
        <AnimatePresence initial={false}>
          {accordionOpen && (
            <motion.ul
              key="acc"
              initial={{ height: 0, opacity: 0 }}
              animate={{ height: "auto", opacity: 1, transition: { height: T.base, opacity: T.fast } }}
              exit={{ height: 0, opacity: 0, transition: { height: T.fast, opacity: T.exit } }}
              className="relative mt-1 overflow-hidden"
            >
              {/* Thread under the parent's icon column. */}
              <span
                aria-hidden
                className="absolute left-[18px] top-1 bottom-1 w-px bg-[var(--color-ink-900)]/[0.08] dark:bg-white/[0.08]"
              />
              {children.map((c) => {
                const active = c === activeChild;
                return (
                  <li key={c.href} className="py-px pl-7">
                    {c.soon ? (
                      <span
                        aria-disabled="true"
                        className="relative flex h-8 cursor-not-allowed items-center gap-2.5 rounded-full px-2.5 text-[12px] font-medium text-[var(--color-ink-400)]"
                      >
                        <span className="flex h-3.5 w-3.5 shrink-0 items-center justify-center opacity-60">{c.icon}</span>
                        <span className="flex-1 truncate">{c.label}</span>
                        <SoonTag />
                      </span>
                    ) : (
                      <Link
                        href={c.href}
                        onClick={onChildNavigate}
                        aria-current={active ? "page" : undefined}
                        className={cn(
                          "relative flex h-8 items-center gap-2.5 rounded-full px-2.5 text-[12px] font-medium outline-none transition-colors duration-150 focus-visible:ring-2 focus-visible:ring-[var(--color-brand-500)]",
                          active
                            ? "text-white"
                            : "text-[var(--color-ink-600)] hover:bg-white/80 hover:text-[var(--color-ink-900)] dark:hover:bg-white/[0.06]",
                        )}
                      >
                        {active && <ActivePill scope={scope} />}
                        <span className="relative flex h-3.5 w-3.5 shrink-0 items-center justify-center">{c.icon}</span>
                        <span className="relative truncate">{c.label}</span>
                      </Link>
                    )}
                  </li>
                );
              })}
            </motion.ul>
          )}
        </AnimatePresence>
      )}

      {/* ── Flyout (collapsed rail) ─────────────────────────────────── */}
      {!expanded && (
        <FloatBeside anchor={triggerRef} open={flyoutOpen} align="top">
          <div
            role="menu"
            aria-label={action.label}
            onMouseEnter={openFlyout}
            onMouseLeave={closeFlyout}
            className="w-[252px] rounded-[18px] border border-[var(--color-ink-100)] bg-[var(--color-popover)] p-2 shadow-[0_24px_60px_-20px_rgba(15,23,42,0.45)] dark:border-white/[0.08]"
          >
            <div className="flex items-center gap-2 px-2 pb-2 pt-1">
              <span className="grid h-7 w-7 place-items-center rounded-lg bg-[var(--color-ink-900)]/[0.05] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
                {action.icon}
              </span>
              <span className="text-[12.5px] font-semibold tracking-tight text-[var(--color-ink-900)]">
                {action.label}
              </span>
            </div>
            <ul className="flex flex-col gap-0.5">
              {children.map((c) => {
                const active = c === activeChild;
                const body = (
                  <>
                    <span
                      className={cn(
                        "grid h-7 w-7 shrink-0 place-items-center rounded-lg",
                        active ? "bg-white/15 text-white" : "bg-[var(--color-ink-900)]/[0.04] dark:bg-white/[0.06]",
                        c.soon && "opacity-60",
                      )}
                    >
                      {c.icon}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-[12.5px] font-semibold tracking-tight">{c.label}</span>
                      {c.hint && (
                        <span className={cn("block truncate text-[10.5px]", active ? "text-white/70" : "text-[var(--color-ink-500)]")}>
                          {c.hint}
                        </span>
                      )}
                    </span>
                    {c.soon && <SoonTag />}
                  </>
                );
                return (
                  <li key={c.href}>
                    {c.soon ? (
                      <span
                        role="menuitem"
                        aria-disabled="true"
                        className="flex cursor-not-allowed items-center gap-2.5 rounded-xl px-2 py-1.5 text-[var(--color-ink-400)]"
                      >
                        {body}
                      </span>
                    ) : (
                      <Link
                        role="menuitem"
                        href={c.href}
                        onClick={() => {
                          setFlyoutOpen(false);
                          onChildNavigate?.();
                        }}
                        className={cn(
                          "flex items-center gap-2.5 rounded-xl px-2 py-1.5 transition-colors",
                          active
                            ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
                            : "text-[var(--color-ink-800)] hover:bg-[var(--color-ink-900)]/[0.05] dark:hover:bg-white/[0.08]",
                        )}
                      >
                        {body}
                      </Link>
                    )}
                  </li>
                );
              })}
            </ul>
          </div>
        </FloatBeside>
      )}
    </div>
  );
}
