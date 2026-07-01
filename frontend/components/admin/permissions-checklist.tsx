"use client";

import { ChevronDown, ChevronRight, RefreshCw, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { type PermissionDto } from "@/lib/api/iam";
import { cn } from "@/lib/utils";

/**
 * Phase S.7 — inline checkbox checklist for granted permissions. Reused
 * by both `/admin/systems/[key]` and `/admin/roles/[name]` so the grant
 * / revoke UX is identical regardless of principal type.
 *
 * - **`granted`** — permission codes currently held by the principal.
 * - **`catalog`** — every row in the static `iam.permissions` table.
 *   `null` while loading; component still renders synthetic rows + a
 *   warning banner if `catalogError` is also set.
 * - **`syntheticPermissions`** — extra rows to inject in front of the
 *   catalog. Used by the system page to surface the runtime-resolved
 *   standard templates (`dtms:source:{key}:order:read|write`) that
 *   aren't in the static catalog. Pass `[]` for the role page.
 * - **`onToggle`** — fired when the user ticks/unticks a row. Receives
 *   the code and the **previous** granted state.
 * - **`hint`** — optional override for the helper sentence under the
 *   header. Defaults to a generic message.
 *
 * Default expand rule: any group that contains at least one granted
 * permission is open; groups with zero grants are collapsed. Override
 * via the chevron click. When the user types in the filter box, every
 * group with a match force-expands so results never get hidden behind
 * a folded section.
 */
export function PermissionsChecklist({
  granted,
  catalog,
  catalogError,
  toggling,
  onToggle,
  syntheticPermissions = [],
  hint,
}: {
  granted: readonly string[];
  catalog: PermissionDto[] | null;
  catalogError: string | null;
  toggling: Set<string>;
  onToggle: (code: string, currentlyGranted: boolean) => void;
  syntheticPermissions?: PermissionDto[];
  hint?: string;
}) {
  const [search, setSearch] = useState("");
  const [expandedOverrides, setExpandedOverrides] = useState<Record<string, boolean>>({});

  const grantedSet = useMemo(() => new Set(granted), [granted]);

  const grouped = useMemo(() => {
    const all: PermissionDto[] = [];
    const seen = new Set<string>();
    for (const p of [...syntheticPermissions, ...(catalog ?? [])]) {
      if (seen.has(p.code)) continue;
      seen.add(p.code);
      all.push(p);
    }

    const map = new Map<string, PermissionDto[]>();
    for (const p of all) {
      const list = map.get(p.module) ?? [];
      list.push(p);
      map.set(p.module, list);
    }
    // Sort: synthetic modules (typically "Source") first, then alphabetical.
    const syntheticModules = new Set(syntheticPermissions.map((p) => p.module));
    const sortedModules = Array.from(map.keys()).sort((a, b) => {
      const aS = syntheticModules.has(a);
      const bS = syntheticModules.has(b);
      if (aS && !bS) return -1;
      if (!aS && bS) return 1;
      return a.localeCompare(b);
    });
    return sortedModules.map((module) => ({
      module,
      rows: map.get(module)!.sort((a, b) => a.code.localeCompare(b.code)),
    }));
  }, [catalog, syntheticPermissions]);

  // Codes the principal holds that don't appear in catalog or synthetics —
  // wildcards (`dtms:*`, `dtms:source:*`) and legacy explicit grants. Surface
  // as an "Other" group so they're still revocable.
  const orphanGranted = useMemo(() => {
    const known = new Set<string>();
    for (const g of grouped) for (const p of g.rows) known.add(p.code);
    return granted.filter((code) => !known.has(code));
  }, [granted, grouped]);

  const isModuleExpanded = (module: string, hasGranted: boolean) => {
    if (module in expandedOverrides) return expandedOverrides[module];
    return hasGranted;
  };

  const toggleModule = (module: string, currentlyExpanded: boolean) =>
    setExpandedOverrides((prev) => ({ ...prev, [module]: !currentlyExpanded }));

  const trimmedSearch = search.trim().toLowerCase();
  const matchesSearch = (p: PermissionDto) =>
    trimmedSearch.length === 0 ||
    p.code.toLowerCase().includes(trimmedSearch) ||
    p.description.toLowerCase().includes(trimmedSearch);

  return (
    <section className="rounded-xl border border-[var(--color-ink-100)] bg-white/70 p-5 dark:border-white/[0.06] dark:bg-white/[0.03]">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-[13px] font-semibold text-[var(--color-ink-800)]">Permissions</h2>
          <p className="mt-1 text-[11px] text-[var(--color-ink-500)]">
            {hint ?? "Tick a row to grant; untick to revoke. Changes apply on the next request — no cache to flush."}
          </p>
        </div>
        <div className="relative shrink-0">
          <Search
            className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)]"
            strokeWidth={2.2}
          />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter…"
            className="w-44 rounded border border-[var(--color-ink-200)] bg-white py-1.5 pl-7 pr-2 text-[12px] focus:border-[var(--color-brand-400)] focus:outline-none dark:border-white/[0.08] dark:bg-white/[0.04]"
          />
        </div>
      </div>

      {catalogError && (
        <div className="mt-3 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-[11.5px] text-amber-800 dark:border-amber-500/40 dark:bg-amber-950/30 dark:text-amber-300">
          Catalog load failed: {catalogError}.{" "}
          {syntheticPermissions.length > 0 ? "Synthetic rows are still shown below." : ""}
        </div>
      )}

      <div className="mt-4 space-y-2">
        {grouped.map(({ module, rows }) => {
          const filtered = rows.filter(matchesSearch);
          const grantedInModule = rows.filter((p) => grantedSet.has(p.code)).length;
          const expanded = isModuleExpanded(module, grantedInModule > 0);
          const forceOpen = trimmedSearch.length > 0 && filtered.length > 0;
          const showRows = expanded || forceOpen;
          if (filtered.length === 0 && trimmedSearch.length > 0) return null;

          return (
            <ModuleGroup
              key={module}
              module={module}
              total={rows.length}
              granted={grantedInModule}
              expanded={showRows}
              onToggleExpand={() => toggleModule(module, expanded)}
            >
              <ul className="mt-1 grid grid-cols-1 gap-0.5 md:grid-cols-2">
                {filtered.map((p) => (
                  <PermissionRow
                    key={p.code}
                    code={p.code}
                    description={p.description}
                    granted={grantedSet.has(p.code)}
                    busy={toggling.has(p.code)}
                    onToggle={() => onToggle(p.code, grantedSet.has(p.code))}
                  />
                ))}
              </ul>
            </ModuleGroup>
          );
        })}

        {orphanGranted.length > 0 && (
          <ModuleGroup
            module="Other / wildcards"
            total={orphanGranted.length}
            granted={orphanGranted.length}
            expanded={true}
            onToggleExpand={() => undefined}
            hideChevron
          >
            <ul className="mt-1 grid grid-cols-1 gap-0.5 md:grid-cols-2">
              {orphanGranted
                .filter((c) => trimmedSearch.length === 0 || c.toLowerCase().includes(trimmedSearch))
                .map((code) => (
                  <PermissionRow
                    key={code}
                    code={code}
                    description="Not in catalog — granted directly (likely a wildcard)"
                    granted={true}
                    busy={toggling.has(code)}
                    onToggle={() => onToggle(code, true)}
                  />
                ))}
            </ul>
          </ModuleGroup>
        )}
      </div>
    </section>
  );
}

function ModuleGroup({
  module,
  total,
  granted,
  expanded,
  onToggleExpand,
  hideChevron,
  children,
}: {
  module: string;
  total: number;
  granted: number;
  expanded: boolean;
  onToggleExpand: () => void;
  hideChevron?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div className="rounded border border-[var(--color-ink-100)] bg-white dark:border-white/[0.06] dark:bg-white/[0.02]">
      <button
        type="button"
        onClick={onToggleExpand}
        disabled={hideChevron}
        className="flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left text-[12px] hover:bg-[var(--color-ink-50)] disabled:cursor-default disabled:hover:bg-transparent dark:hover:bg-white/[0.04]"
      >
        <span className="flex items-center gap-1.5">
          {!hideChevron &&
            (expanded ? (
              <ChevronDown className="h-3.5 w-3.5 text-[var(--color-ink-400)]" strokeWidth={2.2} />
            ) : (
              <ChevronRight className="h-3.5 w-3.5 text-[var(--color-ink-400)]" strokeWidth={2.2} />
            ))}
          <span className="font-semibold text-[var(--color-ink-700)]">{module}</span>
          <span className="text-[11px] text-[var(--color-ink-400)]">({total})</span>
        </span>
        <span
          className={cn(
            "rounded-full px-2 py-0.5 text-[10.5px] font-medium",
            granted === 0
              ? "bg-[var(--color-ink-50)] text-[var(--color-ink-500)] dark:bg-white/[0.04] dark:text-[var(--color-ink-400)]"
              : "bg-emerald-50 text-emerald-700 dark:bg-emerald-950/30 dark:text-emerald-300",
          )}
        >
          {granted} granted
        </span>
      </button>
      {expanded && (
        <div className="border-t border-[var(--color-ink-100)] px-3 py-2 dark:border-white/[0.06]">{children}</div>
      )}
    </div>
  );
}

function PermissionRow({
  code,
  description,
  granted,
  busy,
  onToggle,
}: {
  code: string;
  description: string;
  granted: boolean;
  busy: boolean;
  onToggle: () => void;
}) {
  return (
    <li>
      <label
        className={cn(
          "flex cursor-pointer items-start gap-2 rounded px-2 py-1 text-[12px] hover:bg-[var(--color-ink-50)] dark:hover:bg-white/[0.03]",
          busy && "opacity-60",
        )}
      >
        <input
          type="checkbox"
          checked={granted}
          disabled={busy}
          onChange={onToggle}
          className="mt-0.5 h-3.5 w-3.5 rounded border-[var(--color-ink-300)] text-[var(--color-brand-500)] focus:ring-1 focus:ring-[var(--color-brand-400)]"
        />
        <span className="flex min-w-0 flex-1 flex-col">
          <code className="truncate font-mono text-[11.5px] text-[var(--color-ink-700)]">{code}</code>
          {description && (
            <span className="truncate text-[10.5px] text-[var(--color-ink-400)]">{description}</span>
          )}
        </span>
        {busy && (
          <RefreshCw
            className="mt-0.5 h-3 w-3 shrink-0 animate-spin text-[var(--color-ink-400)]"
            strokeWidth={2.2}
          />
        )}
      </label>
    </li>
  );
}
