"use client";

import { Loader2, Plus, Truck } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import { PermissionGuard } from "@/components/auth/permission-guard";
import {
  Pagination,
  type PageSize,
} from "@/components/delivery-orders/pagination";
import {
  DataTableBody,
  DataTableHead,
  DataTableShell,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table/table-shell";
import { TableEmptyState } from "@/components/primitives/data-table/table-empty-state";
import { GlassCard } from "@/components/primitives/glass-card";
import { getCarrierTypeProfiles, type CarrierTypeProfile } from "@/lib/api/facility-profiles";
import { getCarriers, type Carrier, type CarrierStatus } from "@/lib/api/fleet-carriers";
import { Permissions } from "@/lib/auth/permissions";
import { cn } from "@/lib/utils";
import { RegisterCarrierDialog } from "./register-carrier-dialog";

const STATUSES: CarrierStatus[] = ["Available", "InUse", "Maintenance", "Retired"];

export function CarriersExperience() {
  return (
    <PermissionGuard requires={Permissions.Fleet.CarrierRead}>
      <Inner />
    </PermissionGuard>
  );
}

function Inner() {
  const { hasPermission } = useAuth();
  const canWrite = hasPermission(Permissions.Fleet.CarrierWrite);

  const [status, setStatus] = useState("");
  const [typeCode, setTypeCode] = useState("");
  const [searchDraft, setSearchDraft] = useState("");
  const [search, setSearch] = useState("");

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<PageSize>(25);

  const [rows, setRows] = useState<Carrier[]>([]);
  const [total, setTotal] = useState(0);
  const [carrierTypes, setCarrierTypes] = useState<CarrierTypeProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [registerOpen, setRegisterOpen] = useState(false);

  const refresh = useCallback(
    (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);
      getCarriers(
        {
          status: status || undefined,
          carrierTypeCode: typeCode || undefined,
          q: search || undefined,
          page,
          pageSize,
        },
        signal,
      )
        .then((res) => {
          setRows(res.data);
          setTotal(res.totalCount);
        })
        .catch((e: Error) => {
          if (e.name !== "AbortError") setError(e.message || "Failed to load carriers");
        })
        .finally(() => setLoading(false));
    },
    [status, typeCode, search, page, pageSize],
  );

  useEffect(() => {
    const ac = new AbortController();
    refresh(ac.signal);
    return () => ac.abort();
  }, [refresh]);

  useEffect(() => {
    getCarrierTypeProfiles()
      .then(setCarrierTypes)
      .catch(() => {
        // The type filter degrades to "All"; the carrier list itself is
        // unaffected, so this is not worth blocking the page over.
      });
  }, []);

  // Any filter change resets to page 1 — otherwise a narrower result set leaves
  // you stranded on page 5 looking at an empty table.
  const applyFilter = (fn: () => void) => {
    fn();
    setPage(1);
  };

  return (
    <div className="space-y-5">
      <header className="flex items-center gap-3">
        <span className="grid h-10 w-10 place-items-center rounded-[14px] bg-gradient-to-br from-[var(--color-pastel-mint)] to-[var(--color-pastel-sky)] text-[var(--color-brand-900)]">
          <Truck className="h-5 w-5" strokeWidth={2.1} />
        </span>
        <div className="flex-1">
          <h1 className="font-display text-[1.35rem] font-semibold text-[var(--color-ink-900)]">
            Carriers
          </h1>
          <p className="text-[12.5px] text-[var(--color-ink-500)]">
            Racks and carts — what exists, what state each is in, where it was last seen.
          </p>
        </div>
        {canWrite && (
          <button type="button" onClick={() => setRegisterOpen(true)} className={registerBtn}>
            <Plus className="h-3.5 w-3.5" strokeWidth={2.4} />
            Register
          </button>
        )}
      </header>

      <GlassCard className="flex flex-wrap items-center gap-2 px-4 py-3">
        <select
          value={status}
          onChange={(e) => applyFilter(() => setStatus(e.target.value))}
          className={inputCls}
        >
          <option value="">All statuses</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>

        <select
          value={typeCode}
          onChange={(e) => applyFilter(() => setTypeCode(e.target.value))}
          className={inputCls}
        >
          <option value="">All carrier types</option>
          {carrierTypes.map((t) => (
            <option key={t.code} value={t.code}>
              {t.code}
            </option>
          ))}
        </select>

        <form
          className="flex flex-1 items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            applyFilter(() => setSearch(searchDraft.trim()));
          }}
        >
          <input
            value={searchDraft}
            onChange={(e) => setSearchDraft(e.target.value)}
            placeholder="Search code, name or barcode…"
            className={cn(inputCls, "min-w-[200px] flex-1")}
          />
          <button type="submit" className={secondaryBtn}>
            Search
          </button>
        </form>
      </GlassCard>

      {error ? (
        <GlassCard className="px-6 py-10 text-center">
          <p className="text-[13px] font-medium text-[var(--color-coral)]">{error}</p>
        </GlassCard>
      ) : loading ? (
        <GlassCard className="grid place-items-center px-6 py-16">
          <Loader2 className="h-6 w-6 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.2} />
        </GlassCard>
      ) : rows.length === 0 ? (
        <TableEmptyState
          variant={status || typeCode || search ? "no-filter-match" : "no-data"}
          title="No carriers"
          body={
            status || typeCode || search
              ? "Nothing matches these filters."
              : "Register a carrier to see it here."
          }
          icon={Truck}
        />
      ) : (
        <>
          <DataTableShell>
            <DataTableHead>
              <TableTh>Code</TableTh>
              <TableTh>Type</TableTh>
              <TableTh>Name</TableTh>
              <TableTh>Status</TableTh>
              <TableTh>Last seen</TableTh>
            </DataTableHead>
            <DataTableBody>
              {rows.map((c) => (
                <tr key={c.id} className="border-t border-white/40 dark:border-white/[0.05]">
                  <TableTd>
                    <span className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)]">
                      {c.carrierCode}
                    </span>
                    {c.barcode && (
                      <div className="mt-0.5 font-mono text-[10.5px] text-[var(--color-ink-500)]">
                        {c.barcode}
                      </div>
                    )}
                  </TableTd>
                  <TableTd>
                    <span className="font-mono text-[11.5px] text-[var(--color-ink-600)]">
                      {c.carrierTypeCode}
                    </span>
                  </TableTd>
                  <TableTd>
                    <span className="text-[12.5px] text-[var(--color-ink-800)]">
                      {c.displayName ?? "—"}
                    </span>
                  </TableTd>
                  <TableTd>
                    <StatusChip status={c.status} />
                    {c.status === "Maintenance" && c.maintenanceReason && (
                      <div className="mt-0.5 max-w-[220px] truncate text-[10.5px] text-[var(--color-ink-500)]">
                        {c.maintenanceReason}
                      </div>
                    )}
                    {c.status === "Retired" && c.retireReason && (
                      <div className="mt-0.5 max-w-[220px] truncate text-[10.5px] text-[var(--color-ink-500)]">
                        {c.retireReason}
                      </div>
                    )}
                  </TableTd>
                  <TableTd>
                    {/* Location and its timestamp always travel together — a
                        location with no "when" reads as current when it is not. */}
                    <span className="text-[12.5px] text-[var(--color-ink-800)]">
                      {c.currentLocationCode ?? "—"}
                    </span>
                    {c.lastSeenAt && (
                      <div className="mt-0.5 text-[10.5px] text-[var(--color-ink-500)]">
                        {new Date(c.lastSeenAt).toLocaleString()}
                      </div>
                    )}
                  </TableTd>
                </tr>
              ))}
            </DataTableBody>
          </DataTableShell>

          <GlassCard>
            <Pagination
              total={total}
              page={page}
              pageSize={pageSize}
              onPageChange={setPage}
              onPageSizeChange={(s) => {
                setPageSize(s);
                setPage(1);
              }}
            />
          </GlassCard>
        </>
      )}

      <RegisterCarrierDialog
        open={registerOpen}
        carrierTypes={carrierTypes}
        onClose={() => setRegisterOpen(false)}
        onCreated={() => refresh()}
      />
    </div>
  );
}

function StatusChip({ status }: { status: CarrierStatus }) {
  const tone: Record<CarrierStatus, string> = {
    Available: "bg-[var(--color-pastel-mint)] text-[var(--color-brand-900)]",
    InUse: "bg-[var(--color-pastel-sky)] text-[var(--color-brand-900)]",
    Maintenance: "bg-[var(--color-pastel-butter)] text-[var(--color-ink-800)]",
    Retired: "bg-[var(--color-ink-100)] text-[var(--color-ink-500)] dark:bg-white/[0.06]",
  };
  return (
    <span
      className={cn(
        "rounded-full px-2.5 py-0.5 text-[10.5px] font-semibold",
        tone[status],
      )}
    >
      {status}
    </span>
  );
}

const inputCls =
  "h-9 rounded-md border border-white/70 bg-white/60 px-2.5 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]";

const registerBtn =
  "inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-3 text-[11.5px] font-semibold text-white transition-all hover:shadow-[0_10px_28px_-12px_rgba(15,23,42,0.5)] dark:bg-[var(--color-brand-500)]";

const secondaryBtn =
  "inline-flex h-9 shrink-0 items-center rounded-full bg-white/50 px-4 text-[12px] font-semibold text-[var(--color-ink-700)] transition-colors hover:bg-white/80 dark:bg-white/[0.06] dark:hover:bg-white/[0.12]";
