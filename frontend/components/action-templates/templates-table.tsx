"use client";

import {
  ArrowUpDown,
  ChevronDown,
  ChevronRight,
  ChevronUp,
  MoreHorizontal,
  Pencil,
  Power,
  Sparkles,
  Trash2,
  Workflow,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";
import type { ActionTemplateDto } from "@/lib/api/action-templates";
import { Highlight } from "@/components/delivery-orders/highlight";

export type SortColumn = "actionName" | "actionCategory" | "isActive" | "modifiedAt";
export type SortDir = "asc" | "desc";

type RowAction = "edit" | "toggle" | "delete";

function relativeTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "—";
  const diffMs = Date.now() - d.getTime();
  const min = Math.floor(diffMs / 60000);
  if (min < 1) return "just now";
  if (min < 60) return `${min}m ago`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const day = Math.floor(hr / 24);
  if (day < 7) return `${day}d ago`;
  return d.toLocaleDateString();
}

function paramPreview(t: ActionTemplateDto): { key: string; value: string }[] {
  return t.actionParameters.map((p) => ({
    key: p.key,
    value: p.value == null || p.value === "" ? "—" : String(p.value),
  }));
}

type Props = {
  templates: ActionTemplateDto[];
  loading: boolean;
  busyId: string | null;
  onOpenDetail: (t: ActionTemplateDto) => void;
  onAction: (action: RowAction, t: ActionTemplateDto) => void;
  sortBy: SortColumn;
  sortDir: SortDir;
  onSortChange: (col: SortColumn) => void;
  search: string;
  hasFilters: boolean;
  onClearFilters: () => void;
  onCreate: () => void;
};

export function TemplatesTable({
  templates,
  loading,
  busyId,
  onOpenDetail,
  onAction,
  sortBy,
  sortDir,
  onSortChange,
  search,
  hasFilters,
  onClearFilters,
  onCreate,
}: Props) {
  if (loading && templates.length === 0) return <TableSkeleton />;
  if (!loading && templates.length === 0)
    return (
      <EmptyState
        search={search}
        hasFilters={hasFilters}
        onClearFilters={onClearFilters}
        onCreate={onCreate}
      />
    );

  return (
    <div className="overflow-hidden rounded-[var(--radius-xl)] glass">
      {/* Desktop table */}
      <div className="hidden md:block overflow-x-auto">
        <table className="w-full text-left">
          <thead>
            <tr className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
              <SortableTh col="actionName" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Template
              </SortableTh>
              <SortableTh col="actionCategory" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Category
              </SortableTh>
              <th className="px-3 py-3.5">Parameters</th>
              <SortableTh col="isActive" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Status
              </SortableTh>
              <SortableTh col="modifiedAt" sortBy={sortBy} sortDir={sortDir} onClick={onSortChange}>
                Updated
              </SortableTh>
              <th className="px-5 py-3.5 w-12" />
            </tr>
          </thead>
          <tbody>
            <AnimatePresence initial={false}>
              {templates.map((t, i) => (
                <motion.tr
                  key={t.id}
                  layout
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, transition: { duration: 0.18 } }}
                  transition={{
                    duration: 0.4,
                    delay: Math.min(i * 0.025, 0.4),
                    ease: [0.22, 1, 0.36, 1],
                  }}
                  onClick={() => onOpenDetail(t)}
                  className={cn(
                    "group cursor-pointer border-t border-[var(--color-ink-100)]/60 dark:border-white/[0.04]",
                    "transition-colors duration-150 hover:bg-white/40 dark:hover:bg-white/[0.03]",
                    !t.isActive && "opacity-70",
                  )}
                >
                  <td className="px-5 py-3.5">
                    <div className="font-mono text-[13px] font-semibold text-[var(--color-ink-900)]">
                      <Highlight text={t.actionName} query={search} />
                    </div>
                    <div className="text-[11px] text-[var(--color-ink-400)] font-mono">
                      {t.id.slice(0, 8)}
                    </div>
                  </td>
                  <td className="px-3 py-3.5">
                    <CategoryBadge category={t.actionCategory} />
                  </td>
                  <td className="px-3 py-3.5">
                    <ParamChips params={paramPreview(t)} search={search} />
                  </td>
                  <td className="px-3 py-3.5">
                    <ActiveBadge active={t.isActive} />
                  </td>
                  <td className="px-3 py-3.5">
                    <div className="text-[11.5px] text-[var(--color-ink-700)] whitespace-nowrap">
                      {relativeTime(t.modifiedAt ?? t.createdAt)}
                    </div>
                    {(t.modifiedBy ?? t.createdBy) && (
                      <div className="text-[10.5px] text-[var(--color-ink-400)] truncate max-w-[140px]">
                        by {t.modifiedBy ?? t.createdBy}
                      </div>
                    )}
                  </td>
                  <td className="px-5 py-3.5 text-right">
                    <RowMenu
                      template={t}
                      busy={busyId === t.id}
                      onAction={onAction}
                      onOpenDetail={() => onOpenDetail(t)}
                    />
                  </td>
                </motion.tr>
              ))}
            </AnimatePresence>
          </tbody>
        </table>
      </div>

      {/* Mobile cards */}
      <div className="md:hidden divide-y divide-[var(--color-ink-100)]/60 dark:divide-white/[0.04]">
        <AnimatePresence initial={false}>
          {templates.map((t, i) => (
            <motion.div
              key={t.id}
              layout
              initial={{ opacity: 0, y: 6 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0 }}
              transition={{ duration: 0.35, delay: Math.min(i * 0.03, 0.4) }}
              onClick={() => onOpenDetail(t)}
              className={cn(
                "px-4 py-4 cursor-pointer transition-colors",
                "active:bg-white/40 dark:active:bg-white/[0.03]",
                !t.isActive && "opacity-70",
              )}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <div className="font-mono text-[13px] font-semibold text-[var(--color-ink-900)] truncate">
                      <Highlight text={t.actionName} query={search} />
                    </div>
                    <CategoryBadge category={t.actionCategory} />
                  </div>
                  <div className="mt-2">
                    <ParamChips params={paramPreview(t)} search={search} />
                  </div>
                </div>
                <ChevronRight className="h-4 w-4 shrink-0 text-[var(--color-ink-400)] mt-1" />
              </div>
              <div className="mt-3 flex flex-wrap items-center gap-3 text-[11.5px] text-[var(--color-ink-500)]">
                <ActiveBadge active={t.isActive} />
                <span className="ml-auto">
                  {relativeTime(t.modifiedAt ?? t.createdAt)}
                </span>
              </div>
            </motion.div>
          ))}
        </AnimatePresence>
      </div>
    </div>
  );
}

// ── Cells ──────────────────────────────────────────────────────────────

function CategoryBadge({ category }: { category: ActionTemplateDto["actionCategory"] }) {
  const isAct = category === "Act";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
        isAct
          ? "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]"
          : "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]",
      )}
    >
      {category.toUpperCase()}
    </span>
  );
}

function ActiveBadge({ active }: { active: boolean }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
        active
          ? "bg-[var(--color-success-soft)] text-[var(--color-success)]"
          : "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06] dark:text-[var(--color-ink-400)]",
      )}
    >
      <span
        className={cn(
          "relative inline-flex h-1.5 w-1.5 rounded-full",
          active ? "bg-[var(--color-success)]" : "bg-[var(--color-ink-300)]",
        )}
      >
        {active && (
          <motion.span
            className="absolute inset-0 rounded-full bg-[var(--color-success)]"
            animate={{ scale: [1, 2.2, 1], opacity: [0.55, 0, 0.55] }}
            transition={{ duration: 1.8, repeat: Infinity, ease: "easeOut" }}
          />
        )}
      </span>
      {active ? "Active" : "Inactive"}
    </span>
  );
}

function ParamChips({
  params,
  search,
}: {
  params: { key: string; value: string }[];
  search: string;
}) {
  if (params.length === 0)
    return (
      <span className="text-[11px] italic text-[var(--color-ink-400)]">No parameters</span>
    );
  const visible = params.slice(0, 3);
  const overflow = params.length - visible.length;
  return (
    <div className="flex flex-wrap gap-1 max-w-[300px]">
      {visible.map((p, i) => (
        <span
          key={`${p.key}-${i}`}
          className="inline-flex items-center gap-1 rounded-[8px] bg-white/65 px-1.5 py-0.5 text-[10.5px] font-mono dark:bg-white/[0.06]"
        >
          <span className="text-[var(--color-ink-500)]">
            <Highlight text={p.key} query={search} />
          </span>
          <span className="text-[var(--color-ink-300)]">=</span>
          <span className="font-semibold text-[var(--color-ink-800)]">
            <Highlight text={p.value} query={search} />
          </span>
        </span>
      ))}
      {overflow > 0 && (
        <span className="inline-flex items-center rounded-[8px] bg-white/45 px-1.5 py-0.5 text-[10.5px] font-mono text-[var(--color-ink-500)] dark:bg-white/[0.04]">
          +{overflow}
        </span>
      )}
    </div>
  );
}

// ── Row menu (mirrors orders-table RowMenu) ────────────────────────────

function RowMenu({
  template,
  busy,
  onAction,
  onOpenDetail,
}: {
  template: ActionTemplateDto;
  busy: boolean;
  onAction: (a: RowAction, t: ActionTemplateDto) => void;
  onOpenDetail: () => void;
}) {
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (!open) return;
    const handle = () => setOpen(false);
    const t = setTimeout(() => window.addEventListener("click", handle), 0);
    return () => {
      clearTimeout(t);
      window.removeEventListener("click", handle);
    };
  }, [open]);

  return (
    <div className="relative inline-block" onClick={(e) => e.stopPropagation()}>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        disabled={busy}
        className="rounded-md p-1.5 text-[var(--color-ink-500)] opacity-0 transition-all group-hover:opacity-100 focus-visible:opacity-100 hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] disabled:opacity-30 dark:hover:bg-white/10"
        aria-label={`Actions for ${template.actionName}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <MoreHorizontal className="h-4 w-4" strokeWidth={2.2} />
      </button>
      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, scale: 0.94, y: -4 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.96, y: -4, transition: { duration: 0.12 } }}
            transition={{ type: "spring", stiffness: 460, damping: 30 }}
            role="menu"
            aria-label={`Actions for ${template.actionName}`}
            className="absolute right-0 z-20 mt-1 w-48 origin-top-right overflow-hidden rounded-xl glass-strong shadow-[0_20px_50px_-15px_rgba(15,23,42,0.35)]"
          >
            <MenuItem
              onClick={() => {
                setOpen(false);
                onOpenDetail();
              }}
            >
              <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.2} />
              View details
            </MenuItem>
            <MenuItem
              onClick={() => {
                setOpen(false);
                onAction("edit", template);
              }}
            >
              <Pencil className="h-3.5 w-3.5" strokeWidth={2.2} />
              Edit template
            </MenuItem>
            <MenuItem
              tone={template.isActive ? "default" : "success"}
              onClick={() => {
                setOpen(false);
                onAction("toggle", template);
              }}
            >
              <Power className="h-3.5 w-3.5" strokeWidth={2.2} />
              {template.isActive ? "Deactivate" : "Activate"}
            </MenuItem>
            <MenuItem
              tone="coral"
              onClick={() => {
                setOpen(false);
                onAction("delete", template);
              }}
            >
              <Trash2 className="h-3.5 w-3.5" strokeWidth={2.2} />
              Delete template
            </MenuItem>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function MenuItem({
  onClick,
  tone = "default",
  children,
}: {
  onClick: () => void;
  tone?: "default" | "brand" | "success" | "coral";
  children: React.ReactNode;
}) {
  const tones = {
    default: "text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] dark:hover:bg-white/10",
    brand: "text-[var(--color-brand-500)] hover:bg-[var(--color-pastel-sky)]",
    success: "text-[var(--color-success)] hover:bg-[var(--color-success-soft)]",
    coral: "text-[var(--color-coral)] hover:bg-[#fde0db] dark:hover:bg-[#3a1a17]",
  };
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex w-full items-center gap-2 px-3 py-2 text-left text-[12.5px] font-semibold transition-colors",
        tones[tone],
      )}
    >
      {children}
    </button>
  );
}

// ── Sortable headers ────────────────────────────────────────────────────

function SortableTh({
  col,
  sortBy,
  sortDir,
  onClick,
  align = "left",
  children,
}: {
  col: SortColumn;
  sortBy: SortColumn;
  sortDir: SortDir;
  onClick: (col: SortColumn) => void;
  align?: "left" | "right";
  children: React.ReactNode;
}) {
  const active = sortBy === col;
  const Icon = !active ? ArrowUpDown : sortDir === "asc" ? ChevronUp : ChevronDown;
  return (
    <th
      className={cn("px-3 py-3.5", align === "right" && "text-right")}
      aria-sort={active ? (sortDir === "asc" ? "ascending" : "descending") : "none"}
    >
      <button
        type="button"
        onClick={() => onClick(col)}
        aria-label={`Sort by ${typeof children === "string" ? children : col}${
          active ? `, currently ${sortDir === "asc" ? "ascending" : "descending"}` : ""
        }`}
        className={cn(
          "group inline-flex items-center gap-1 transition-colors duration-150",
          align === "right" && "flex-row-reverse",
          active
            ? "text-[var(--color-ink-900)]"
            : "text-[var(--color-ink-400)] hover:text-[var(--color-ink-700)]",
        )}
      >
        <span>{children}</span>
        <Icon
          className={cn(
            "h-3 w-3 transition-opacity",
            active ? "opacity-100" : "opacity-50 group-hover:opacity-80",
          )}
          strokeWidth={2.4}
        />
      </button>
    </th>
  );
}

// ── Skeleton + Empty ────────────────────────────────────────────────────

function TableSkeleton() {
  return (
    <div className="rounded-[var(--radius-xl)] glass p-2">
      <div className="px-3 py-3 flex gap-3 text-[10.5px] uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        <span>Loading templates…</span>
      </div>
      <div className="space-y-2 px-2 pb-2">
        {Array.from({ length: 6 }).map((_, i) => (
          <motion.div
            key={i}
            initial={{ opacity: 0 }}
            animate={{ opacity: [0.4, 0.7, 0.4] }}
            transition={{ duration: 1.4, repeat: Infinity, delay: i * 0.06 }}
            className="h-12 rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.04]"
          />
        ))}
      </div>
    </div>
  );
}

function EmptyState({
  search,
  hasFilters,
  onClearFilters,
  onCreate,
}: {
  search: string;
  hasFilters: boolean;
  onClearFilters: () => void;
  onCreate: () => void;
}) {
  const trimmed = search.trim();
  const variant = trimmed
    ? "no-search-match"
    : hasFilters
      ? "no-filter-match"
      : "no-data";
  const copy = {
    "no-search-match": {
      title: "No templates match your search",
      body: (
        <>
          Nothing found for{" "}
          <span className="font-mono font-semibold text-[var(--color-ink-700)]">
            “{trimmed}”
          </span>
          . Check the spelling or widen the type filter.
        </>
      ),
    },
    "no-filter-match": {
      title: "No templates in this view",
      body: <>The current filters returned no rows. Clear them to see everything.</>,
    },
    "no-data": {
      title: "No templates yet",
      body: <>Use the “New template” button to compose your first building block.</>,
    },
  }[variant];

  return (
    <div className="rounded-[var(--radius-xl)] glass px-6 py-16 text-center">
      <motion.div
        animate={{ y: [0, -4, 0] }}
        transition={{ duration: 3, repeat: Infinity, ease: "easeInOut" }}
        className="mx-auto grid h-16 w-16 place-items-center rounded-[20px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[var(--color-pastel-lavender)] text-[var(--color-brand-900)]"
      >
        {variant === "no-data" ? (
          <Workflow className="h-6 w-6" strokeWidth={2} />
        ) : (
          <Sparkles className="h-6 w-6" strokeWidth={2} />
        )}
      </motion.div>
      <h3 className="font-display mt-5 text-lg font-semibold text-[var(--color-ink-900)]">
        {copy.title}
      </h3>
      <p className="mt-1 text-[13px] text-[var(--color-ink-500)] mx-auto max-w-sm">
        {copy.body}
      </p>
      {hasFilters && variant !== "no-data" ? (
        <motion.button
          type="button"
          onClick={onClearFilters}
          whileHover={{ y: -1 }}
          whileTap={{ scale: 0.97 }}
          className="mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12px] font-semibold text-white transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]"
        >
          Clear filters
        </motion.button>
      ) : variant === "no-data" ? (
        <motion.button
          type="button"
          onClick={onCreate}
          whileHover={{ y: -1 }}
          whileTap={{ scale: 0.97 }}
          className="mt-5 inline-flex items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-4 py-2 text-[12px] font-semibold text-white transition-all hover:shadow-[0_14px_36px_-12px_rgba(15,23,42,0.6)] dark:bg-[var(--color-brand-500)]"
        >
          Create your first template
        </motion.button>
      ) : null}
    </div>
  );
}
