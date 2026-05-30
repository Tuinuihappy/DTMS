"use client";

import {
  Boxes,
  CircleDot,
  Circle,
  Cog,
  Cloud,
  Library,
  PackageCheck,
  Sparkles,
  type LucideIcon,
} from "lucide-react";

import { cn } from "@/lib/utils";

// Sidebar mirrors Adobe Creative Cloud's left rail: section labels in
// muted caps, item rows with a leading icon, and a small badge slot for
// counts. The selected state uses a tinted background instead of a
// border — feels softer, more "macOS Finder".
//
// State is owned by the page (selectedFilter / setSelectedFilter). The
// sidebar is a pure presentational component so the page can swap in
// query-derived counts.

export type NavFilter =
  | "all"
  | "active"
  | "inactive"
  | "actions"
  | "orders"
  | "delivery-orders";

// Legacy alias for backwards compatibility — existing imports referencing
// `TemplateFilter` continue to compile while the new code uses NavFilter.
export type TemplateFilter = NavFilter;

interface SidebarProps {
  selected: NavFilter;
  onSelect: (filter: NavFilter) => void;
  counts?: Partial<Record<NavFilter, number>>;
}

interface SidebarItem {
  id: NavFilter;
  label: string;
  icon: LucideIcon;
}

const FILTER_GROUP: SidebarItem[] = [
  { id: "all", label: "All", icon: Boxes },
  { id: "active", label: "Active", icon: CircleDot },
  { id: "inactive", label: "Inactive", icon: Circle },
];

const LIBRARY_GROUP: SidebarItem[] = [
  { id: "actions", label: "ActionTemplates", icon: Sparkles },
  { id: "orders", label: "OrderTemplates", icon: Library },
];

const OPERATIONS_GROUP: SidebarItem[] = [
  { id: "delivery-orders", label: "Delivery Orders", icon: PackageCheck },
];

export function Sidebar({ selected, onSelect, counts = {} }: SidebarProps) {
  return (
    <aside className="liquid-glass relative flex h-full flex-col rounded-[24px] p-3">
      {/* Brand row — small chip + name. The Sparkles puck is a leftover
          from the floating header; here it just brands the rail. */}
      <div className="relative z-[2] mb-4 flex items-center gap-2.5 px-2 pt-1">
        <div className="liquid-puck liquid-puck-primary flex h-8 w-8 items-center justify-center rounded-2xl">
          <Sparkles className="relative z-[2] h-3.5 w-3.5" strokeWidth={2.25} />
        </div>
        <div className="min-w-0">
          <p className="text-[13px] font-semibold tracking-tight leading-tight">
            DTMS
          </p>
          <p className="text-[10px] text-muted-foreground leading-tight">
            Templates
          </p>
        </div>
      </div>

      <SidebarGroup label="Templates">
        {FILTER_GROUP.map((item) => (
          <SidebarRow
            key={item.id}
            item={item}
            selected={selected === item.id}
            count={counts[item.id]}
            onClick={() => onSelect(item.id)}
          />
        ))}
      </SidebarGroup>

      <SidebarGroup label="Library">
        {LIBRARY_GROUP.map((item) => (
          <SidebarRow
            key={item.id}
            item={item}
            selected={selected === item.id}
            count={counts[item.id]}
            onClick={() => onSelect(item.id)}
          />
        ))}
      </SidebarGroup>

      <SidebarGroup label="Operations">
        {OPERATIONS_GROUP.map((item) => (
          <SidebarRow
            key={item.id}
            item={item}
            selected={selected === item.id}
            count={counts[item.id]}
            onClick={() => onSelect(item.id)}
          />
        ))}
      </SidebarGroup>

      <SidebarGroup label="Settings">
        <SidebarRow
          item={{ id: "all" as NavFilter, label: "RIOT3 connection", icon: Cloud }}
          selected={false}
          // Placeholder — no settings page yet. Disabled-but-visible so
          // the IA hints at where it'll live.
          onClick={() => {}}
          disabled
        />
        <SidebarRow
          item={{ id: "all" as NavFilter, label: "Preferences", icon: Cog }}
          selected={false}
          onClick={() => {}}
          disabled
        />
      </SidebarGroup>
    </aside>
  );
}

function SidebarGroup({
  label,
  children,
}: {
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="relative z-[2] mb-4">
      <p className="mb-1.5 px-2 text-[10px] font-semibold uppercase tracking-[0.08em] text-muted-foreground/80">
        {label}
      </p>
      <div className="space-y-0.5">{children}</div>
    </div>
  );
}

function SidebarRow({
  item,
  selected,
  count,
  onClick,
  disabled,
}: {
  item: SidebarItem;
  selected: boolean;
  count?: number;
  onClick: () => void;
  disabled?: boolean;
}) {
  const Icon = item.icon;
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        "press-feedback group flex w-full items-center gap-2.5 rounded-xl px-2.5 py-1.5 text-left transition-colors disabled:opacity-40",
        selected
          ? "bg-primary/12 text-primary"
          : "text-foreground hover:bg-black/[0.04] dark:hover:bg-white/[0.05]"
      )}
    >
      <Icon
        className={cn(
          "h-3.5 w-3.5 shrink-0",
          selected ? "text-primary" : "text-muted-foreground"
        )}
        strokeWidth={2.25}
      />
      <span className="flex-1 truncate text-[13px] font-medium tracking-tight">
        {item.label}
      </span>
      {count !== undefined && count > 0 ? (
        <span
          className={cn(
            "rounded-full px-1.5 py-0.5 text-[10px] font-semibold leading-none",
            selected
              ? "bg-primary/20 text-primary"
              : "bg-black/[0.05] text-muted-foreground dark:bg-white/[0.08]"
          )}
        >
          {count}
        </span>
      ) : null}
    </button>
  );
}
