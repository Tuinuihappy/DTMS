"use client";

import { Images, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { AttachmentGallery } from "@/components/attachments/attachment-gallery";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import type { AttachmentOwner } from "@/lib/api/fleet-attachments";

export type PhotosTarget = {
  owner: AttachmentOwner;
  ownerId: string;
  /** What the photos are of, e.g. a carrier code. */
  label: string;
};

/**
 * Photos for one thing, opened on demand.
 *
 * Deliberately a dialog rather than a column of thumbnails in the table: a
 * gallery per row would fire one list request per row on every page load, and
 * both tables it serves are the hottest query on their page.
 */
export function PhotosDialog({
  target,
  canEdit,
  onClose,
}: {
  target: PhotosTarget | null;
  canEdit: boolean;
  onClose: () => void;
}) {
  return (
    <>
      <OverlayBackdrop
        open={target !== null}
        onClick={onClose}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {target && (
          <div
            key="photos-dialog"
            className="pointer-events-none fixed inset-0 z-50 flex items-center justify-center p-4"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.94, y: 12 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.96, y: 12, transition: { duration: 0.16 } }}
              transition={{ type: "spring", stiffness: 360, damping: 30 }}
              className="pointer-events-auto relative w-full max-w-lg overflow-hidden rounded-[var(--radius-xl)] glass-strong"
            >
              <header className="flex items-start gap-3 px-6 pt-5">
                <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-pastel-sky)] text-[var(--color-brand-900)]">
                  <Images className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <div className="flex-1">
                  <h2 className="font-display text-[1.15rem] font-semibold text-[var(--color-ink-900)]">
                    Photos
                  </h2>
                  <p className="font-mono text-[11.5px] text-[var(--color-ink-500)]">
                    {target.label}
                  </p>
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

              <div className="px-6 pb-6 pt-4">
                <AttachmentGallery
                  key={`${target.owner}:${target.ownerId}`}
                  owner={target.owner}
                  ownerId={target.ownerId}
                  canEdit={canEdit}
                  emptyHint="No photos yet."
                />
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
