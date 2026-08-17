"use client";

import { Boxes, Loader2, Plus, Layers } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import { PermissionGuard } from "@/components/auth/permission-guard";
import {
  DataTableBody,
  DataTableHead,
  DataTableShell,
  TableTd,
  TableTh,
} from "@/components/primitives/data-table/table-shell";
import { TableEmptyState } from "@/components/primitives/data-table/table-empty-state";
import { GlassCard } from "@/components/primitives/glass-card";
import {
  getCarrierTypeProfiles,
  getLoadUnitProfiles,
  type CarrierTypeProfile,
  type LoadUnitProfile,
} from "@/lib/api/facility-profiles";
import { Permissions } from "@/lib/auth/permissions";
import { cn } from "@/lib/utils";
import {
  RegisterCarrierProfileDialog,
  RegisterLoadUnitProfileDialog,
} from "./register-profile-dialogs";

export function FacilityProfilesExperience() {
  return (
    <PermissionGuard requires={Permissions.Facility.ProfileRead}>
      <Inner />
    </PermissionGuard>
  );
}

function Inner() {
  const { hasPermission } = useAuth();
  const canWrite = hasPermission(Permissions.Facility.ProfileWrite);

  const [carriers, setCarriers] = useState<CarrierTypeProfile[]>([]);
  const [carrierFilter, setCarrierFilter] = useState("");
  const [loadUnits, setLoadUnits] = useState<LoadUnitProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [carrierDialog, setCarrierDialog] = useState(false);
  const [loadUnitDialog, setLoadUnitDialog] = useState(false);

  const loadUnitsFor = useCallback((code: string) => {
    getLoadUnitProfiles(code || undefined)
      .then(setLoadUnits)
      .catch((e: Error) => setError(e.message));
  }, []);

  const reloadCarriers = useCallback(() => {
    getCarrierTypeProfiles()
      .then(setCarriers)
      .catch((e: Error) => setError(e.message));
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    Promise.all([getCarrierTypeProfiles(), getLoadUnitProfiles()])
      .then(([c, l]) => {
        if (cancelled) return;
        setCarriers(c);
        setLoadUnits(l);
      })
      .catch((e: Error) => !cancelled && setError(e.message))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="space-y-5">
      <header className="flex items-center gap-3">
        <span className="grid h-10 w-10 place-items-center rounded-[14px] bg-gradient-to-br from-[var(--color-pastel-mint)] to-[var(--color-pastel-sky)] text-[var(--color-brand-900)]">
          <Layers className="h-5 w-5" strokeWidth={2.1} />
        </span>
        <div>
          <h1 className="font-display text-[1.35rem] font-semibold text-[var(--color-ink-900)]">
            Facility reference
          </h1>
          <p className="text-[12.5px] text-[var(--color-ink-500)]">
            Carrier & load-unit profiles.
          </p>
        </div>
      </header>

      {error ? (
        <GlassCard className="px-6 py-10 text-center">
          <p className="text-[13px] font-medium text-[var(--color-coral)]">{error}</p>
        </GlassCard>
      ) : loading ? (
        <GlassCard className="grid place-items-center px-6 py-16">
          <Loader2 className="h-6 w-6 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.2} />
        </GlassCard>
      ) : (
        <>
          <Section
            title="Carrier type profiles"
            icon={Boxes}
            count={carriers.length}
            aside={
              canWrite ? (
                <button type="button" onClick={() => setCarrierDialog(true)} className={registerBtn}>
                  <Plus className="h-3.5 w-3.5" strokeWidth={2.4} />
                  Register
                </button>
              ) : undefined
            }
          >
            {carriers.length === 0 ? (
              <TableEmptyState variant="no-data" title="No carrier profiles" body="Register carrier types to see them here." icon={Boxes} />
            ) : (
              <DataTableShell>
                <DataTableHead>
                  <TableTh>Code</TableTh>
                  <TableTh>Name</TableTh>
                  <TableTh>AMR capability</TableTh>
                  <TableTh align="right">Max weight</TableTh>
                  <TableTh align="right">Slots</TableTh>
                </DataTableHead>
                <DataTableBody>
                  {carriers.map((c) => (
                    <tr key={c.id} className="border-t border-white/40 dark:border-white/[0.05]">
                      <TableTd>
                        <span className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)]">{c.code}</span>
                      </TableTd>
                      <TableTd>
                        <div className="text-[12.5px] text-[var(--color-ink-800)]">{c.displayName}</div>
                        {c.description && (
                          <div className="mt-0.5 max-w-[280px] truncate text-[11px] text-[var(--color-ink-500)]">{c.description}</div>
                        )}
                      </TableTd>
                      <TableTd>
                        <span className="rounded-full bg-[var(--color-pastel-lavender)] px-2.5 py-0.5 text-[10.5px] font-semibold text-[var(--color-brand-900)]">
                          {c.aMRCapability}
                        </span>
                      </TableTd>
                      <TableTd align="right">
                        <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-700)]">
                          {c.maxWeightKg != null ? `${c.maxWeightKg} kg` : "—"}
                        </span>
                      </TableTd>
                      <TableTd align="right">
                        <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-700)]">
                          {c.maxSlots ?? "—"}
                        </span>
                      </TableTd>
                    </tr>
                  ))}
                </DataTableBody>
              </DataTableShell>
            )}
          </Section>

          <Section
            title="Load unit profiles"
            icon={Boxes}
            count={loadUnits.length}
            aside={
              <div className="flex items-center gap-2">
                <select
                value={carrierFilter}
                onChange={(e) => {
                  setCarrierFilter(e.target.value);
                  loadUnitsFor(e.target.value);
                }}
                className={inputClass}
              >
                <option value="">All carrier types</option>
                {carriers.map((c) => (
                  <option key={c.code} value={c.code}>
                    {c.code}
                  </option>
                ))}
              </select>
                {canWrite && (
                  <button type="button" onClick={() => setLoadUnitDialog(true)} className={registerBtn}>
                    <Plus className="h-3.5 w-3.5" strokeWidth={2.4} />
                    Register
                  </button>
                )}
              </div>
            }
          >
            {loadUnits.length === 0 ? (
              <TableEmptyState
                variant={carrierFilter ? "no-filter-match" : "no-data"}
                title="No load-unit profiles"
                body={carrierFilter ? "None for this carrier type." : "Register load units to see them here."}
                icon={Boxes}
              />
            ) : (
              <DataTableShell>
                <DataTableHead>
                  <TableTh>Code</TableTh>
                  <TableTh>Name</TableTh>
                  <TableTh>Carrier</TableTh>
                  <TableTh align="right">Dimensions (mm)</TableTh>
                  <TableTh align="right">Max gross</TableTh>
                </DataTableHead>
                <DataTableBody>
                  {loadUnits.map((l) => (
                    <tr key={l.id} className="border-t border-white/40 dark:border-white/[0.05]">
                      <TableTd>
                        <span className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)]">{l.code}</span>
                      </TableTd>
                      <TableTd>
                        <span className="text-[12.5px] text-[var(--color-ink-800)]">{l.displayName}</span>
                      </TableTd>
                      <TableTd>
                        <span className="font-mono text-[11.5px] text-[var(--color-ink-600)]">{l.carrierTypeCode}</span>
                      </TableTd>
                      <TableTd align="right">
                        <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-700)]">
                          {l.lengthMm}×{l.widthMm}×{l.heightMm}
                        </span>
                      </TableTd>
                      <TableTd align="right">
                        <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-700)]">{l.maxGrossWeightKg} kg</span>
                      </TableTd>
                    </tr>
                  ))}
                </DataTableBody>
              </DataTableShell>
            )}
          </Section>
        </>
      )}

      <RegisterCarrierProfileDialog
        open={carrierDialog}
        onClose={() => setCarrierDialog(false)}
        onCreated={reloadCarriers}
      />
      <RegisterLoadUnitProfileDialog
        open={loadUnitDialog}
        carriers={carriers}
        onClose={() => setLoadUnitDialog(false)}
        onCreated={() => loadUnitsFor(carrierFilter)}
      />
    </div>
  );
}

function Section({
  title,
  icon: Icon,
  count,
  aside,
  children,
}: {
  title: string;
  icon: React.ComponentType<{ className?: string; strokeWidth?: number }>;
  count: number;
  aside?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 text-[13px] font-semibold text-[var(--color-ink-800)]">
          <Icon className="h-4 w-4 text-[var(--color-ink-500)]" strokeWidth={2.1} />
          {title}
          <span className="rounded-full bg-white/60 px-2 py-0.5 text-[10.5px] font-mono text-[var(--color-ink-500)] dark:bg-white/[0.06]">
            {count}
          </span>
        </div>
        {aside}
      </div>
      {children}
    </section>
  );
}

const inputClass =
  "h-9 rounded-md border border-white/70 bg-white/60 px-2.5 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur-md focus:border-[var(--color-brand-500)]/30 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/40 dark:border-white/10 dark:bg-white/[0.05]";

const registerBtn =
  "inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-3 text-[11.5px] font-semibold text-white transition-all hover:shadow-[0_10px_28px_-12px_rgba(15,23,42,0.5)] dark:bg-[var(--color-brand-500)]";
