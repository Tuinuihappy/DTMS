"use client";

import { Download, Plus, RefreshCw, Search, X } from "lucide-react";
import { motion } from "motion/react";
import type { OrderStatus, Priority, TransportMode } from "@/lib/api/delivery-orders";
import { cn } from "@/lib/utils";
import { FilterChip } from "./badges";

export type StatusFilter = "All" | "Active" | "Completed" | "Terminal" | OrderStatus;

// Ordered by lifecycle: overview → intake → planning → execution → terminal.
// Tones mirror StatusBadge so the chip and the row badge agree at a glance.
type ChipTone = "ink" | "sky" | "lavender" | "mint" | "peach" | "amber" | "success" | "coral";

const QUICK_FILTERS: { key: StatusFilter; label: string; tone: ChipTone }[] = [
  // Overview
  { key: "All", label: "All", tone: "ink" },
  { key: "Active", label: "Active", tone: "amber" },
  { key: "Terminal", label: "Terminal", tone: "coral" },
  // Intake
  { key: "Draft", label: "Draft", tone: "ink" },
  { key: "Submitted", label: "Submitted", tone: "sky" },
  { key: "Validated", label: "Validated", tone: "sky" },
  // Planning
  { key: "Confirmed", label: "Confirmed", tone: "lavender" },
  { key: "Planning", label: "Planning", tone: "lavender" },
  { key: "Planned", label: "Planned", tone: "mint" },
  // Execution
  { key: "Dispatched", label: "Dispatched", tone: "peach" },
  { key: "InProgress", label: "In progress", tone: "peach" },
  // Outcomes
  { key: "Completed", label: "Completed", tone: "success" },
  { key: "PartiallyCompleted", label: "Partial", tone: "amber" },
  { key: "Held", label: "Held", tone: "amber" },
  { key: "Failed", label: "Failed", tone: "coral" },
  { key: "Amended", label: "Amended", tone: "ink" },
  { key: "Cancelled", label: "Cancelled", tone: "ink" },
  { key: "Rejected", label: "Rejected", tone: "coral" },
];

export function FilterBar({
  status,
  onStatusChange,
  counts,
  search,
  onSearchChange,
  priority,
  onPriorityChange,
  transportMode,
  onTransportModeChange,
  onCreate,
  onExport,
  onRefresh,
  refreshing,
}: {
  status: StatusFilter;
  onStatusChange: (s: StatusFilter) => void;
  counts: Partial<Record<StatusFilter, number>>;
  search: string;
  onSearchChange: (s: string) => void;
  priority: Priority | "All";
  onPriorityChange: (p: Priority | "All") => void;
  transportMode: TransportMode | "All";
  onTransportModeChange: (m: TransportMode | "All") => void;
  onCreate: () => void;
  onExport: () => void;
  onRefresh: () => void;
  refreshing: boolean;
}) {
  return (
    <div className="space-y-3 sm:space-y-4">
      {/* Toolbar: search + actions */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="relative flex-1 sm:max-w-md">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--color-ink-400)]"
            strokeWidth={2.2}
          />
          <input
            type="text"
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            placeholder="Search by ref, requester, notes…"
            className={cn(
              "w-full rounded-full bg-white/60 py-2.5 pl-10 pr-9 text-[13px] font-medium",
              "border border-white/70 backdrop-blur-md transition-all duration-200",
              "placeholder:text-[var(--color-ink-400)] text-[var(--color-ink-900)]",
              "focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 focus:border-[var(--color-brand-500)]/30",
              "dark:bg-white/[0.05] dark:border-white/10",
            )}
          />
          {search && (
            <button
              type="button"
              onClick={() => onSearchChange("")}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 rounded-full p-1 text-[var(--color-ink-400)] transition-colors hover:bg-[var(--color-ink-100)] dark:hover:bg-white/10"
              aria-label="Clear search"
            >
              <X className="h-3.5 w-3.5" strokeWidth={2.4} />
            </button>
          )}
        </div>

        <div className="flex items-center gap-2">
          <ToolbarButton onClick={onRefresh} title="Refresh" disabled={refreshing}>
            <RefreshCw
              className={cn("h-3.5 w-3.5", refreshing && "animate-spin")}
              strokeWidth={2.4}
            />
            <span className="hidden sm:inline">Refresh</span>
          </ToolbarButton>
          <ToolbarButton onClick={onExport} title="Export CSV">
            <Download className="h-3.5 w-3.5" strokeWidth={2.4} />
            <span className="hidden sm:inline">Export</span>
          </ToolbarButton>
          <motion.button
            type="button"
            onClick={onCreate}
            whileHover={{ y: -1 }}
            whileTap={{ scale: 0.97 }}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-full px-3.5 py-2 text-[12px] font-semibold",
              "bg-[var(--color-brand-900)] text-white shadow-[0_10px_28px_-12px_rgba(15,23,42,0.45)]",
              "transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)]",
              "dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]",
            )}
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2.6} />
            New order
          </motion.button>
        </div>
      </div>

      {/* Status chips */}
      <div className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1 [&::-webkit-scrollbar]:h-1">
        {QUICK_FILTERS.map((f) => (
          <FilterChip
            key={f.key}
            active={status === f.key}
            tone={f.tone}
            count={counts[f.key]}
            onClick={() => onStatusChange(f.key)}
          >
            {f.label}
          </FilterChip>
        ))}
        <div className="mx-1 self-center h-5 w-px bg-[var(--color-ink-200)]/60 dark:bg-white/10" />
        {(["All", "Critical", "High", "Normal", "Low"] as const).map((p) => (
          <FilterChip
            key={p}
            active={priority === p}
            tone={p === "Critical" ? "coral" : p === "High" ? "amber" : "ink"}
            onClick={() => onPriorityChange(p)}
          >
            {p === "All" ? "Any priority" : p}
          </FilterChip>
        ))}
        <div className="mx-1 self-center h-5 w-px bg-[var(--color-ink-200)]/60 dark:bg-white/10" />
        {(["All", "Amr", "Manual", "Fleet"] as const).map((m) => (
          <FilterChip
            key={m}
            active={transportMode === m}
            tone={m === "Amr" ? "sky" : m === "Fleet" ? "mint" : "ink"}
            onClick={() => onTransportModeChange(m)}
          >
            {m === "All" ? "Any transport" : m === "Amr" ? "AMR" : m}
          </FilterChip>
        ))}
      </div>
    </div>
  );
}

function ToolbarButton({
  onClick,
  title,
  disabled,
  children,
}: {
  onClick: () => void;
  title: string;
  disabled?: boolean;
  children: React.ReactNode;
}) {
  return (
    <motion.button
      type="button"
      onClick={onClick}
      title={title}
      disabled={disabled}
      whileHover={{ y: -1 }}
      whileTap={{ scale: 0.97 }}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-3 py-2 text-[12px] font-semibold",
        "bg-white/60 text-[var(--color-ink-700)] border border-white/70",
        "transition-all hover:bg-white/90",
        "dark:bg-white/[0.05] dark:text-[var(--color-ink-700)] dark:border-white/10 dark:hover:bg-white/[0.1]",
        "disabled:opacity-50 disabled:cursor-not-allowed",
      )}
    >
      {children}
    </motion.button>
  );
}
