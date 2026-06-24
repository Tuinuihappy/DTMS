"use client";

import {
  ChevronRight,
  Copy,
  MoreHorizontal,
  Pencil,
  Power,
  Sparkles,
  Trash2,
  Workflow,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useRef, useState } from "react";
import { cn } from "@/lib/utils";
import type { ActionTemplateDto } from "@/lib/api/action-templates";
import { Highlight } from "@/components/delivery-orders/highlight";
import { DateTime } from "@/components/primitives/date-time";
import { RowMenuPortal } from "@/components/primitives/row-menu-portal";
import {
  DataRow,
  DataTableBody,
  DataTableHead,
  DataTableShell,
  MobileCardRow,
  SortableTh,
  TableEmptyState,
  TableSkeleton,
  TableTd,
  TableTh,
  resolveEmptyStateVariant,
} from "@/components/primitives/data-table";

export type SortColumn = "actionName" | "actionCategory" | "isActive" | "modifiedAt";
export type SortDir = "asc" | "desc";

type RowAction = "edit" | "duplicate" | "toggle" | "delete";

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
  if (loading && templates.length === 0)
    return <TableSkeleton label="Loading templates…" />;
  if (!loading && templates.length === 0)
    return (
      <TemplatesEmptyState
        search={search}
        hasFilters={hasFilters}
        onClearFilters={onClearFilters}
        onCreate={onCreate}
      />
    );

  return (
    <>
      {/* Desktop table — primitives handle padding/header/sort/keyboard/focus. */}
      <DataTableShell className="hidden md:block">
        <DataTableHead>
          <SortableTh col="actionName" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Template
          </SortableTh>
          <SortableTh col="actionCategory" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Category
          </SortableTh>
          <TableTh>Parameters</TableTh>
          <SortableTh col="isActive" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Status
          </SortableTh>
          <SortableTh col="modifiedAt" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Updated
          </SortableTh>
          <TableTh className="w-12" aria-label="Actions">
            <span className="sr-only">Actions</span>
          </TableTh>
        </DataTableHead>
        <DataTableBody>
          <AnimatePresence initial={false}>
            {templates.map((t, i) => (
              <DataRow
                key={t.id}
                delayIndex={i}
                disabled={!t.isActive}
                onClick={() => onOpenDetail(t)}
              >
                <TableTd>
                  <div className="font-mono text-[13px] font-semibold text-[var(--color-ink-900)]">
                    <Highlight text={t.actionName} query={search} />
                  </div>
                  <div className="text-[11px] text-[var(--color-ink-400)] font-mono">
                    {t.id.slice(0, 8)}
                  </div>
                </TableTd>
                <TableTd>
                  <CategoryBadge category={t.actionCategory} />
                </TableTd>
                <TableTd>
                  <ParamChips params={paramPreview(t)} search={search} />
                </TableTd>
                <TableTd>
                  <ActiveBadge active={t.isActive} />
                </TableTd>
                <TableTd>
                  <DateTime
                    value={t.modifiedAt ?? t.createdAt}
                    variant="relative"
                    className="text-[11.5px] text-[var(--color-ink-700)] whitespace-nowrap"
                  />
                  {(t.modifiedBy ?? t.createdBy) && (
                    <div
                      className="text-[10.5px] text-[var(--color-ink-400)] truncate max-w-[140px]"
                      title={`by ${t.modifiedBy ?? t.createdBy}`}
                    >
                      by {t.modifiedBy ?? t.createdBy}
                    </div>
                  )}
                </TableTd>
                <TableTd align="right">
                  <RowMenu
                    template={t}
                    busy={busyId === t.id}
                    onAction={onAction}
                    onOpenDetail={() => onOpenDetail(t)}
                  />
                </TableTd>
              </DataRow>
            ))}
          </AnimatePresence>
        </DataTableBody>
      </DataTableShell>

      {/* Mobile cards — separate glass card so the table primitives stay
          table-shaped. Same domain content, different layout. */}
      <div className="md:hidden overflow-hidden rounded-[var(--radius-xl)] glass divide-y divide-[var(--color-ink-100)]/60 dark:divide-white/[0.04]">
        <AnimatePresence initial={false}>
          {templates.map((t, i) => (
            <MobileCardRow
              key={t.id}
              delayIndex={i}
              disabled={!t.isActive}
              ariaLabel={`Action template ${t.actionName} — ${t.isActive ? "active" : "inactive"}`}
              onClick={() => onOpenDetail(t)}
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
                <DateTime
                  value={t.modifiedAt ?? t.createdAt}
                  variant="relative"
                  className="ml-auto"
                />
              </div>
            </MobileCardRow>
          ))}
        </AnimatePresence>
      </div>
    </>
  );
}

// ── Empty state ────────────────────────────────────────────────────────

function TemplatesEmptyState({
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
  const variant = resolveEmptyStateVariant(search, hasFilters);
  const trimmed = search.trim();

  if (variant === "no-search-match") {
    return (
      <TableEmptyState
        variant={variant}
        icon={Sparkles}
        title="No templates match your search"
        body={
          <>
            Nothing found for{" "}
            <span className="font-mono font-semibold text-[var(--color-ink-700)]">
              “{trimmed}”
            </span>
            . Check the spelling or widen the type filter.
          </>
        }
        action={
          hasFilters
            ? { label: "Clear filters", onClick: onClearFilters }
            : undefined
        }
      />
    );
  }

  if (variant === "no-filter-match") {
    return (
      <TableEmptyState
        variant={variant}
        icon={Sparkles}
        title="No templates in this view"
        body="The current filters returned no rows. Clear them to see everything."
        action={{ label: "Clear filters", onClick: onClearFilters }}
      />
    );
  }

  return (
    <TableEmptyState
      variant={variant}
      icon={Workflow}
      title="No templates yet"
      body="Use the “New template” button to compose your first building block."
      action={{ label: "Create your first template", onClick: onCreate }}
    />
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

// ── Row menu ───────────────────────────────────────────────────────────

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
  const triggerRef = useRef<HTMLButtonElement>(null);

  return (
    <div className="inline-block" onClick={(e) => e.stopPropagation()}>
      <button
        ref={triggerRef}
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
      <RowMenuPortal
        open={open}
        onClose={() => setOpen(false)}
        triggerRef={triggerRef}
        width={192}
        ariaLabel={`Actions for ${template.actionName}`}
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
          onClick={() => {
            setOpen(false);
            onAction("duplicate", template);
          }}
        >
          <Copy className="h-3.5 w-3.5" strokeWidth={2.2} />
          Duplicate template
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
      </RowMenuPortal>
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
