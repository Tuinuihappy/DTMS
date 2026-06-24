"use client";

import {
  ChevronRight,
  Copy,
  FileStack,
  MoreHorizontal,
  Pencil,
  Power,
  Rocket,
  Sparkles,
  Trash2,
  Truck,
  Workflow,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import Link from "next/link";
import { useRef, useState } from "react";
import { cn } from "@/lib/utils";
import type { OrderTemplateDto } from "@/lib/api/order-templates";
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

export type SortColumn = "name" | "priority" | "missions" | "isActive" | "modifiedAt";
export type SortDir = "asc" | "desc";

type RowAction = "toggle" | "delete";

type Props = {
  templates: OrderTemplateDto[];
  loading: boolean;
  busyId: string | null;
  onOpenDetail: (t: OrderTemplateDto) => void;
  onDispatch: (t: OrderTemplateDto) => void;
  onAction: (action: RowAction, t: OrderTemplateDto) => void;
  sortBy: SortColumn;
  sortDir: SortDir;
  onSortChange: (col: SortColumn) => void;
  search: string;
  hasFilters: boolean;
  onClearFilters: () => void;
};

export function OrderTemplatesTable({
  templates,
  loading,
  busyId,
  onOpenDetail,
  onDispatch,
  onAction,
  sortBy,
  sortDir,
  onSortChange,
  search,
  hasFilters,
  onClearFilters,
}: Props) {
  if (loading && templates.length === 0)
    return <TableSkeleton label="Loading templates…" />;
  if (!loading && templates.length === 0)
    return (
      <OrderTemplatesEmptyState
        search={search}
        hasFilters={hasFilters}
        onClearFilters={onClearFilters}
      />
    );

  return (
    <>
      {/* Desktop table — primitives handle padding/header/sort/keyboard/focus. */}
      <DataTableShell className="hidden md:block">
        <DataTableHead>
          <SortableTh col="name" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Template
          </SortableTh>
          <SortableTh col="priority" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Priority
          </SortableTh>
          <SortableTh col="missions" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Missions
          </SortableTh>
          <TableTh>Binding</TableTh>
          <SortableTh col="isActive" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Status
          </SortableTh>
          <SortableTh col="modifiedAt" sortBy={sortBy} sortDir={sortDir} onSort={onSortChange}>
            Updated
          </SortableTh>
          <TableTh className="w-20" aria-label="Actions">
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
                  <div className="flex items-center gap-2.5">
                    <span className="grid h-8 w-8 shrink-0 place-items-center rounded-[10px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[var(--color-pastel-lavender)] text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.85)]">
                      <FileStack className="h-3.5 w-3.5" strokeWidth={2.2} />
                    </span>
                    <div className="min-w-0">
                      <div className="text-[13px] font-semibold text-[var(--color-ink-900)]">
                        <Highlight text={t.name} query={search} />
                      </div>
                      {t.description && (
                        <div
                          className="mt-0.5 max-w-[280px] truncate text-[11px] text-[var(--color-ink-500)]"
                          title={t.description}
                        >
                          <Highlight text={t.description} query={search} />
                        </div>
                      )}
                    </div>
                  </div>
                </TableTd>
                <TableTd>
                  <PriorityChip value={t.priority} />
                </TableTd>
                <TableTd>
                  <MissionsSummary template={t} />
                </TableTd>
                <TableTd>
                  <BindingChip template={t} search={search} />
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
                  <div className="flex items-center justify-end gap-1.5">
                    {t.isActive && (
                      <motion.button
                        type="button"
                        whileTap={{ scale: 0.94 }}
                        onClick={(e) => {
                          e.stopPropagation();
                          onDispatch(t);
                        }}
                        title={`Create order from ${t.name}`}
                        className="inline-flex cursor-pointer items-center gap-1 rounded-full bg-[var(--color-brand-900)] px-2.5 py-1 text-[10.5px] font-bold uppercase tracking-[0.08em] text-white opacity-0 shadow-[0_4px_12px_-4px_rgba(15,23,42,0.45)] transition-all group-hover:opacity-100 focus-visible:opacity-100 hover:-translate-y-0.5 dark:bg-[var(--color-brand-500)]"
                      >
                        <Rocket className="h-3 w-3" strokeWidth={2.4} />
                        Create
                      </motion.button>
                    )}
                    <RowMenu
                      template={t}
                      busy={busyId === t.id}
                      onAction={onAction}
                      onOpenDetail={() => onOpenDetail(t)}
                      onDispatch={() => onDispatch(t)}
                    />
                  </div>
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
              ariaLabel={`Order template ${t.name} — priority ${t.priority}${t.isActive ? "" : ", inactive"}`}
              onClick={() => onOpenDetail(t)}
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="grid h-7 w-7 shrink-0 place-items-center rounded-[8px] bg-gradient-to-br from-[var(--color-pastel-sky)] to-[var(--color-pastel-lavender)] text-[var(--color-brand-900)]">
                      <FileStack className="h-3 w-3" strokeWidth={2.2} />
                    </span>
                    <div className="text-[13px] font-semibold text-[var(--color-ink-900)] truncate">
                      <Highlight text={t.name} query={search} />
                    </div>
                    <PriorityChip value={t.priority} />
                  </div>
                  {t.description && (
                    <div className="mt-1.5 text-[11.5px] text-[var(--color-ink-500)] line-clamp-2">
                      <Highlight text={t.description} query={search} />
                    </div>
                  )}
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <MissionsSummary template={t} />
                    <BindingChip template={t} search={search} compact />
                  </div>
                </div>
                <ChevronRight className="mt-1 h-4 w-4 shrink-0 text-[var(--color-ink-400)]" />
              </div>
              <div className="mt-3 flex flex-wrap items-center gap-3 text-[11.5px] text-[var(--color-ink-500)]">
                <ActiveBadge active={t.isActive} />
                <DateTime
                  value={t.modifiedAt ?? t.createdAt}
                  variant="relative"
                  className="ml-auto"
                />
              </div>
              {t.isActive && (
                <motion.button
                  type="button"
                  whileTap={{ scale: 0.96 }}
                  onClick={(e) => {
                    e.stopPropagation();
                    onDispatch(t);
                  }}
                  className="mt-3 inline-flex w-full cursor-pointer items-center justify-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-3 py-2 text-[11.5px] font-semibold text-white dark:bg-[var(--color-brand-500)]"
                >
                  <Rocket className="h-3.5 w-3.5" strokeWidth={2.3} />
                  Create order from this template
                </motion.button>
              )}
            </MobileCardRow>
          ))}
        </AnimatePresence>
      </div>
    </>
  );
}

// ── Cells ──────────────────────────────────────────────────────────────

function PriorityChip({ value }: { value: number }) {
  const tone =
    value >= 8
      ? "bg-[var(--color-coral)]/15 text-[var(--color-coral)]"
      : value >= 5
        ? "bg-[var(--color-amber-soft)] text-[#8a4a07] dark:text-[var(--color-amber)]"
        : "bg-[var(--color-pastel-sky)] text-[var(--color-pastel-sky-ink)]";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
        tone,
      )}
    >
      P{value}
    </span>
  );
}

function MissionsSummary({ template }: { template: OrderTemplateDto }) {
  const missions = template.transportOrder?.missions ?? [];
  const moves = missions.filter((m) => m.type === "MOVE").length;
  const acts = missions.length - moves;
  const structure = template.transportOrder?.structureType ?? "sequence";
  return (
    <div className="flex items-center gap-1.5">
      <span className="inline-flex items-center gap-1 rounded-[8px] bg-white/65 px-1.5 py-0.5 text-[10.5px] font-mono font-semibold dark:bg-white/[0.06]">
        <span className="text-[var(--color-ink-500)]">total</span>
        <span className="text-[var(--color-ink-800)]">{missions.length}</span>
      </span>
      {moves > 0 && (
        <span className="inline-flex items-center gap-1 rounded-[8px] bg-[var(--color-pastel-mint)]/70 px-1.5 py-0.5 text-[10.5px] font-mono font-semibold text-[var(--color-pastel-mint-ink)]">
          MOVE {moves}
        </span>
      )}
      {acts > 0 && (
        <span className="inline-flex items-center gap-1 rounded-[8px] bg-[var(--color-pastel-peach)]/70 px-1.5 py-0.5 text-[10.5px] font-mono font-semibold text-[var(--color-pastel-peach-ink)]">
          ACT {acts}
        </span>
      )}
      <span
        className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]"
        title={`Structure: ${structure}`}
      >
        · {structure}
      </span>
    </div>
  );
}

function BindingChip({
  template,
  search,
  compact = false,
}: {
  template: OrderTemplateDto;
  search: string;
  compact?: boolean;
}) {
  const vehicle = template.appointVehicleName ?? template.appointVehicleKey;
  const group = template.appointVehicleGroupName ?? template.appointVehicleGroupKey;
  const wait = template.appointQueueWaitArea;
  const any = vehicle || group || wait;
  if (!any) {
    return (
      <span className="text-[10.5px] italic text-[var(--color-ink-400)]">
        unbound
      </span>
    );
  }
  return (
    <div className={cn("flex items-center gap-1", compact && "flex-wrap")}>
      {vehicle && (
        <span className="inline-flex items-center gap-1 rounded-full bg-white/65 px-2 py-0.5 text-[10.5px] font-semibold text-[var(--color-ink-700)] dark:bg-white/[0.06]">
          <Truck className="h-3 w-3" strokeWidth={2.3} />
          <Highlight text={vehicle} query={search} />
        </span>
      )}
      {!vehicle && group && (
        <span className="inline-flex items-center gap-1 rounded-full bg-white/65 px-2 py-0.5 text-[10.5px] font-semibold text-[var(--color-ink-700)] dark:bg-white/[0.06]">
          group · <Highlight text={group} query={search} />
        </span>
      )}
      {wait && (
        <span className="rounded-full bg-white/55 px-2 py-0.5 text-[10px] font-mono text-[var(--color-ink-500)] dark:bg-white/[0.04]">
          @{wait}
        </span>
      )}
    </div>
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

// ── Row menu ───────────────────────────────────────────────────────────

function RowMenu({
  template,
  busy,
  onAction,
  onOpenDetail,
  onDispatch,
}: {
  template: OrderTemplateDto;
  busy: boolean;
  onAction: (a: RowAction, t: OrderTemplateDto) => void;
  onOpenDetail: () => void;
  onDispatch: () => void;
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
        className="cursor-pointer rounded-md p-1.5 text-[var(--color-ink-500)] transition-all hover:bg-[var(--color-ink-100)] hover:text-[var(--color-ink-900)] disabled:opacity-30 dark:hover:bg-white/10"
        aria-label={`Actions for ${template.name}`}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <MoreHorizontal className="h-4 w-4" strokeWidth={2.2} />
      </button>
      <RowMenuPortal
        open={open}
        onClose={() => setOpen(false)}
        triggerRef={triggerRef}
        width={208}
        ariaLabel={`Actions for ${template.name}`}
      >
        {template.isActive && (
          <MenuItem
            tone="brand"
            onClick={() => {
              setOpen(false);
              onDispatch();
            }}
          >
            <Rocket className="h-3.5 w-3.5" strokeWidth={2.2} />
            Create order from template
          </MenuItem>
        )}
        <MenuItem
          onClick={() => {
            setOpen(false);
            onOpenDetail();
          }}
        >
          <ChevronRight className="h-3.5 w-3.5" strokeWidth={2.2} />
          View details
        </MenuItem>
        <MenuLink href={`/delivery-orders/order-templates/${template.id}/edit`}>
          <Pencil className="h-3.5 w-3.5" strokeWidth={2.2} />
          Edit template
        </MenuLink>
        <MenuLink href={`/delivery-orders/order-templates/${template.id}/duplicate`}>
          <Copy className="h-3.5 w-3.5" strokeWidth={2.2} />
          Duplicate template
        </MenuLink>
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

const MENU_TONES = {
  default: "text-[var(--color-ink-700)] hover:bg-[var(--color-ink-100)] dark:hover:bg-white/10",
  brand: "text-[var(--color-brand-900)] hover:bg-[var(--color-pastel-sky)] dark:text-[var(--color-brand-500)]",
  success: "text-[var(--color-success)] hover:bg-[var(--color-success-soft)]",
  coral: "text-[var(--color-coral)] hover:bg-[var(--color-coral-soft)]",
} as const;

function MenuItem({
  onClick,
  tone = "default",
  children,
}: {
  onClick: () => void;
  tone?: keyof typeof MENU_TONES;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex w-full cursor-pointer items-center gap-2 px-3 py-2 text-left text-[12.5px] font-semibold transition-colors",
        MENU_TONES[tone],
      )}
    >
      {children}
    </button>
  );
}

function MenuLink({
  href,
  children,
}: {
  href: string;
  children: React.ReactNode;
}) {
  return (
    <Link
      href={href}
      className={cn(
        "flex w-full items-center gap-2 px-3 py-2 text-left text-[12.5px] font-semibold transition-colors",
        MENU_TONES.default,
      )}
    >
      {children}
    </Link>
  );
}

// ── Empty state ────────────────────────────────────────────────────────

function OrderTemplatesEmptyState({
  search,
  hasFilters,
  onClearFilters,
}: {
  search: string;
  hasFilters: boolean;
  onClearFilters: () => void;
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
            . Check the spelling or widen the status filter.
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

  // no-data — primitive supports a custom-rendered CTA via a Link, but the
  // primitive's action prop only takes onClick. Render a custom variant
  // here so the "Author your first template" deep-link stays a real <a>
  // (preserves middle-click + open-in-new-tab).
  return (
    <TableEmptyState
      variant={variant}
      icon={Workflow}
      title="No order templates yet"
      body={
        <>
          Templates let your team dispatch standard delivery flows in one click.
          Compose a recipe of MOVEs and ACTs to get started.{" "}
          <Link
            href="/delivery-orders/order-templates/new"
            className="font-semibold text-[var(--color-brand-500)] hover:underline"
          >
            Author your first template →
          </Link>
        </>
      }
    />
  );
}
