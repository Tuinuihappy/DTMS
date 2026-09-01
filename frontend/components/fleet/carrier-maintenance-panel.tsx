"use client";

import { Loader2, Wrench, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { AttachmentGallery } from "@/components/attachments/attachment-gallery";
import { useAuth } from "@/components/auth/auth-provider";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { Permissions } from "@/lib/auth/permissions";
import {
  getCarrierMaintenanceHistory,
  type CarrierMaintenanceEntry,
} from "@/lib/api/fleet-carriers";
import { cn } from "@/lib/utils";

/**
 * Every repair episode for one carrier, newest first — the record that made the
 * maintenance log worth a table of its own (ADR-019). Status alone answers "is
 * it broken now"; only this answers "how often does this cart break".
 */
export function CarrierMaintenancePanel({
  carrierCode,
  onClose,
}: {
  carrierCode: string | null;
  onClose: () => void;
}) {
  const open = carrierCode !== null;
  const { hasPermission } = useAuth();
  const canEdit = hasPermission(Permissions.Fleet.CarrierWrite);
  const [entries, setEntries] = useState<CarrierMaintenanceEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!carrierCode) return;
    const ac = new AbortController();
    setLoading(true);
    setError(null);
    getCarrierMaintenanceHistory(carrierCode, ac.signal)
      .then(setEntries)
      .catch((e: Error) => {
        if (e.name !== "AbortError") setError(e.message);
      })
      .finally(() => setLoading(false));
    return () => ac.abort();
  }, [carrierCode]);

  return (
    <>
      <OverlayBackdrop
        open={open}
        onClick={onClose}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {open && (
          <motion.aside
            key="carrier-maintenance-panel"
            initial={{ opacity: 0, x: 32 }}
            animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: 32, transition: { duration: 0.16 } }}
            transition={{ type: "spring", stiffness: 340, damping: 32 }}
            className="fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col overflow-hidden glass-strong"
          >
            <header className="flex items-start gap-3 border-b border-white/40 px-6 py-5 dark:border-white/[0.06]">
              <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-pastel-butter)] text-[var(--color-ink-800)]">
                <Wrench className="h-5 w-5" strokeWidth={2.2} />
              </span>
              <div className="flex-1">
                <h2 className="font-display text-[1.1rem] font-semibold text-[var(--color-ink-900)]">
                  Maintenance history
                </h2>
                <p className="font-mono text-[11.5px] text-[var(--color-ink-500)]">{carrierCode}</p>
              </div>
              <button
                type="button"
                onClick={onClose}
                className="rounded-full p-2 text-[var(--color-ink-500)] transition-colors hover:bg-white/40 hover:text-[var(--color-ink-900)] dark:hover:bg-white/10"
                aria-label="Close"
              >
                <X className="h-4 w-4" strokeWidth={2.4} />
              </button>
            </header>

            <div className="flex-1 overflow-y-auto px-6 py-5">
              {loading ? (
                <div className="grid place-items-center py-16">
                  <Loader2
                    className="h-6 w-6 animate-spin text-[var(--color-ink-400)]"
                    strokeWidth={2.2}
                  />
                </div>
              ) : error ? (
                <p className="text-[13px] font-medium text-[var(--color-coral)]">{error}</p>
              ) : entries.length === 0 ? (
                <p className="text-[12.5px] text-[var(--color-ink-500)]">
                  This carrier has never been sent for maintenance.
                </p>
              ) : (
                <ol className="space-y-3">
                  {entries.map((e) => (
                    <li
                      key={e.id}
                      className={cn(
                        "rounded-[var(--radius-lg)] border px-4 py-3",
                        e.isOpen
                          ? "border-[var(--color-pastel-butter)] bg-[var(--color-pastel-butter)]/40"
                          : "border-white/50 bg-white/40 dark:border-white/[0.06] dark:bg-white/[0.03]",
                      )}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <span className="text-[12.5px] font-semibold text-[var(--color-ink-900)]">
                          {e.reason}
                        </span>
                        {e.isOpen && (
                          <span className="shrink-0 rounded-full bg-[var(--color-ink-900)] px-2 py-0.5 text-[10px] font-semibold text-white">
                            Open
                          </span>
                        )}
                      </div>

                      <div className="mt-1.5 text-[11px] text-[var(--color-ink-500)]">
                        {new Date(e.startedAt).toLocaleString()} · {e.startedBy}
                        {e.endedAt && (
                          <>
                            {" → "}
                            {new Date(e.endedAt).toLocaleString()}
                            {e.endedBy ? ` · ${e.endedBy}` : ""}
                          </>
                        )}
                      </div>

                      {e.outcome && (
                        <p className="mt-1.5 text-[11.5px] text-[var(--color-ink-700)]">
                          {e.outcome}
                        </p>
                      )}

                      {/* Damage photos belong to the episode, not the carrier:
                          "what was broken that time" is a different question
                          from "what does this cart look like". */}
                      <div className="mt-3 border-t border-white/40 pt-3 dark:border-white/[0.06]">
                        <AttachmentGallery
                          owner="maintenance"
                          ownerId={e.id}
                          canEdit={canEdit}
                          emptyHint="No photos for this repair."
                        />
                      </div>
                    </li>
                  ))}
                </ol>
              )}
            </div>
          </motion.aside>
        )}
      </AnimatePresence>
    </>
  );
}
