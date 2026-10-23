"use client";

import { Boxes, Images, Loader2, Pencil, Plus, Layers, Trash2 } from "lucide-react";
import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { PhotoCell } from "@/components/attachments/photo-cell";
import { PhotosDialog, type PhotosTarget } from "@/components/attachments/photos-dialog";
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
import { useToast } from "@/components/primitives/toast";
import {
  getCarrierTypeProfiles,
  type CarrierTypeProfile,
} from "@/lib/api/fleet-carrier-types";
import { Permissions } from "@/lib/auth/permissions";
import { cn } from "@/lib/utils";
import { CarrierTypeDeleteDialog } from "./carrier-type-delete-dialog";
import { CarrierTypeFormDialog } from "./carrier-type-form-dialog";

export function CarrierTypesExperience() {
  return (
    <PermissionGuard requires={Permissions.Fleet.CarrierTypeRead}>
      <Inner />
    </PermissionGuard>
  );
}

function Inner() {
  const { hasPermission } = useAuth();
  const canWrite = hasPermission(Permissions.Fleet.CarrierTypeWrite);
  const canDelete = hasPermission(Permissions.Fleet.CarrierTypeDelete);

  const [carriers, setCarriers] = useState<CarrierTypeProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // null closes the form; a profile opens it in edit mode. Kept apart from the
  // open flag so "register" and "edit CART type" cannot both be half-true.
  const [carrierDialog, setCarrierDialog] = useState(false);
  const [editing, setEditing] = useState<CarrierTypeProfile | null>(null);
  const [deleting, setDeleting] = useState<CarrierTypeProfile | null>(null);
  const [photos, setPhotos] = useState<PhotosTarget | null>(null);

  // A reload after save keeps the table on screen (dimmed) and, if it fails,
  // reports it as a banner above the table — it used to replace the whole
  // page with the error even though the data shown was still good.
  const [reloading, setReloading] = useState(false);
  const reloadCarriers = useCallback(() => {
    setReloading(true);
    getCarrierTypeProfiles()
      .then((c) => {
        setCarriers(c);
        setError(null);
      })
      .catch((e: Error) => setError(e.message || "Failed to reload carrier types"))
      .finally(() => setReloading(false));
  }, []);

  // Success toast + a one-shot highlight on the saved row (`n` re-keys it so
  // saving the same type twice flashes twice).
  const toast = useToast();
  const [flash, setFlash] = useState<{ code: string; n: number } | null>(null);
  const onSaved = useCallback(
    (code: string) => {
      toast.push({ tone: "success", message: `Carrier type ${code} saved` });
      setFlash((f) => ({ code, n: (f?.n ?? 0) + 1 }));
      reloadCarriers();
    },
    [toast, reloadCarriers],
  );
  const onDeleted = useCallback(() => {
    if (deleting) toast.push({ tone: "success", message: `Carrier type ${deleting.code} deleted` });
    reloadCarriers();
  }, [deleting, toast, reloadCarriers]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    getCarrierTypeProfiles()
      .then((c) => {
        if (cancelled) return;
        setCarriers(c);
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
            Carrier Types
          </h1>
          <p className="text-[12.5px] text-[var(--color-ink-500)]">
            The kinds of rack and cart each carrier is registered against.
          </p>
        </div>
      </header>

      {/* With data already loaded, an error is a banner over it; only a
          failed FIRST load (nothing to show) takes the page. */}
      {error && carriers.length > 0 && (
        <div
          role="alert"
          className="rounded-xl bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]"
        >
          {error}
        </div>
      )}

      {error && carriers.length === 0 ? (
        <GlassCard className="px-6 py-10 text-center">
          <p className="text-[13px] font-medium text-[var(--color-coral)]">{error}</p>
        </GlassCard>
      ) : loading ? (
        <GlassCard className="grid place-items-center px-6 py-16">
          <Loader2 className="h-6 w-6 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.2} />
        </GlassCard>
      ) : (
        <div
          aria-busy={reloading}
          className={cn("transition-opacity duration-150", reloading && "opacity-60")}
        >
          <Section
            title="Carrier type profiles"
            icon={Boxes}
            count={carriers.length}
            aside={
              canWrite ? (
                <button
                  type="button"
                  onClick={() => {
                    setEditing(null);
                    setCarrierDialog(true);
                  }}
                  className={registerBtn}
                >
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
                  <TableTh className="w-14">Photo</TableTh>
                  <TableTh>Name</TableTh>
                  <TableTh>AMR capability</TableTh>
                  <TableTh align="right">Max weight</TableTh>
                  <TableTh align="right">Slots</TableTh>
                  <TableTh align="right">Carriers</TableTh>
                  <TableTh align="right">{""}</TableTh>
                </DataTableHead>
                <DataTableBody>
                  {carriers.map((c) => {
                    const flashed = flash?.code === c.code;
                    return (
                    <tr
                      key={flashed ? `${c.id}:${flash.n}` : c.id}
                      className={cn(
                        "border-t border-white/40 dark:border-white/[0.05]",
                        flashed && "row-flash",
                      )}
                    >
                      <TableTd>
                        {/* Straight to the carriers of this type — the registry
                            page already filters by type, so the code is the
                            natural way in rather than a second filter here. */}
                        <Link
                          href={`/fleet/carriers?type=${encodeURIComponent(c.code)}`}
                          className="font-mono text-[12.5px] font-semibold text-[var(--color-ink-900)] underline-offset-2 hover:underline"
                        >
                          {c.code}
                        </Link>
                      </TableTd>
                      <TableTd className="py-2">
                        <PhotoCell
                          key={`${c.id}:${c.coverAttachmentId}`}
                          owner="carrier-type"
                          ownerId={c.id}
                          label={c.code}
                          coverAttachmentId={c.coverAttachmentId}
                          photoCount={c.photoCount}
                          canEdit={canWrite}
                          onOpen={(focusAttachmentId) =>
                            setPhotos({
                              owner: "carrier-type",
                              ownerId: c.id,
                              label: c.code,
                              focusAttachmentId,
                            })
                          }
                        />
                      </TableTd>
                      <TableTd>
                        <div className="text-[12.5px] text-[var(--color-ink-800)]">{c.displayName}</div>
                        {c.description && (
                          <div className="mt-0.5 max-w-[280px] truncate text-[11px] text-[var(--color-ink-500)]">{c.description}</div>
                        )}
                      </TableTd>
                      <TableTd>
                        <span className="rounded-full bg-[var(--color-pastel-lavender)] px-2.5 py-0.5 text-[10.5px] font-semibold text-[var(--color-brand-900)]">
                          {c.amrCapability}
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
                      <TableTd align="right">
                        {c.carrierCount > 0 ? (
                          <Link
                            href={`/fleet/carriers?type=${encodeURIComponent(c.code)}`}
                            className="font-mono text-[12px] tabular-nums text-[var(--color-ink-700)] underline-offset-2 hover:underline"
                          >
                            {c.carrierCount}
                          </Link>
                        ) : (
                          <span className="font-mono text-[12px] tabular-nums text-[var(--color-ink-400)]">0</span>
                        )}
                      </TableTd>
                      <TableTd align="right">
                        <div className="flex items-center justify-end gap-0.5">
                          {/* Kept beside the cell: the cell opens straight onto the
                              cover, this opens the gallery to browse and manage. */}
                          <button
                            type="button"
                            onClick={() =>
                              setPhotos({ owner: "carrier-type", ownerId: c.id, label: c.code })
                            }
                            className={rowBtn}
                          >
                            <Images className="h-3.5 w-3.5" strokeWidth={2.1} />
                            Photos
                          </button>
                          {canWrite && (
                            <button
                              type="button"
                              onClick={() => {
                                setEditing(c);
                                setCarrierDialog(true);
                              }}
                              className={rowBtn}
                            >
                              <Pencil className="h-3.5 w-3.5" strokeWidth={2.1} />
                              Edit
                            </button>
                          )}
                          {canDelete && (
                            // Disabled rather than hidden while the type is in
                            // use: the button is the only place that explains
                            // why it cannot go, and the count beside it says
                            // what to clear first.
                            <button
                              type="button"
                              onClick={() => setDeleting(c)}
                              disabled={c.carrierCount > 0}
                              title={
                                c.carrierCount > 0
                                  ? `Used by ${c.carrierCount} carrier(s) — move or delete them first`
                                  : `Delete ${c.code}`
                              }
                              className={cn(
                                rowBtn,
                                c.carrierCount > 0
                                  ? "cursor-not-allowed text-[var(--color-ink-300)] hover:bg-transparent hover:text-[var(--color-ink-300)]"
                                  : "text-[var(--color-ink-500)] hover:bg-[var(--color-coral-soft)] hover:text-[var(--color-coral)]",
                              )}
                              aria-label={`Delete ${c.code}`}
                            >
                              <Trash2 className="h-3.5 w-3.5" strokeWidth={2.1} />
                            </button>
                          )}
                        </div>
                      </TableTd>
                    </tr>
                    );
                  })}
                </DataTableBody>
              </DataTableShell>
            )}
          </Section>
        </div>
      )}

      <CarrierTypeFormDialog
        open={carrierDialog}
        carrierType={editing}
        onClose={() => setCarrierDialog(false)}
        onSaved={onSaved}
      />

      <CarrierTypeDeleteDialog
        carrierType={deleting}
        onClose={() => setDeleting(null)}
        onDeleted={onDeleted}
      />

      <PhotosDialog
        target={photos}
        canEdit={canWrite}
        onClose={() => setPhotos(null)}
        // Patch the one row from the gallery's own list rather than refetching,
        // the same as the carriers page.
        onItemsChanged={(list) => {
          const ownerId = photos?.ownerId;
          if (!ownerId) return;
          setCarriers((rows) =>
            rows.map((r) =>
              r.id === ownerId
                ? { ...r, coverAttachmentId: list[0]?.id ?? null, photoCount: list.length }
                : r,
            ),
          );
        }}
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

const rowBtn =
  "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-[11px] font-medium text-[var(--color-ink-600)] transition-colors hover:bg-white/60 hover:text-[var(--color-ink-900)] dark:hover:bg-white/[0.08]";

const registerBtn =
  "inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-3 text-[11.5px] font-semibold text-white transition-all hover:shadow-[0_10px_28px_-12px_rgba(15,23,42,0.5)] dark:bg-[var(--color-brand-500)]";
