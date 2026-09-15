"use client";

import { Images, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { AttachmentGallery } from "@/components/attachments/attachment-gallery";
import { AttachmentLightbox } from "@/components/attachments/attachment-lightbox";
import { useAttachments } from "@/components/attachments/use-attachments";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import type { Attachment, AttachmentOwner } from "@/lib/api/fleet-attachments";

export type PhotosTarget = {
  owner: AttachmentOwner;
  ownerId: string;
  /** What the photos are of, e.g. a carrier code. */
  label: string;
  /** Open straight onto this image full-size, with no gallery behind it;
   *  closing it closes everything. For a thumbnail in a table, where "show me
   *  this photo" is the whole ask. */
  focusAttachmentId?: string | null;
};

/**
 * Photos for one thing, opened on demand — the place to browse them all, add
 * and delete.
 *
 * A table may still show a thumbnail per row without paying a request per row:
 * the list carries each row's cover id and count, and the image comes from a
 * stable address the browser caches (see attachmentThumbnailUrl). What the table
 * must not do is load a gallery per row, which is what this dialog is for.
 *
 * With a focus id it is only a lightbox: one layer, one backdrop, shown at once.
 * Opening the gallery first and the photo over it meant two entrances, two
 * darkening steps and a dialog glimpsed on the way in and out.
 */
export function PhotosDialog({
  target,
  canEdit,
  onClose,
  onItemsChanged,
}: {
  target: PhotosTarget | null;
  canEdit: boolean;
  onClose: () => void;
  /** Lets the caller keep a row's cover and count in step without refetching
   *  its whole list. */
  onItemsChanged?: (items: Attachment[]) => void;
}) {
  const focus = target?.focusAttachmentId ? target : null;
  const gallery = target && !focus ? target : null;

  return (
    <>
      <FocusedPhoto
        target={focus}
        canEdit={canEdit}
        onClose={onClose}
        onItemsChanged={onItemsChanged}
      />

      <OverlayBackdrop
        open={gallery !== null}
        onClick={onClose}
        className="z-40 bg-[var(--color-ink-900)]/55 backdrop-blur-md"
      />
      <AnimatePresence>
        {gallery && (
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
                    {gallery.label}
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
                  key={`${gallery.owner}:${gallery.ownerId}`}
                  owner={gallery.owner}
                  ownerId={gallery.ownerId}
                  canEdit={canEdit}
                  emptyHint="No photos yet."
                  onItemsChanged={onItemsChanged}
                />
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}

/**
 * The lightbox on its own. Always rendered, even with no target, so its
 * backdrop exists before the first open and fades in rather than snapping
 * dark; loads nothing while closed.
 *
 * The list still loads behind the picture: it supplies the details line, lets
 * Delete know the image exists, and hands the table the current cover and count.
 */
function FocusedPhoto({
  target,
  canEdit,
  onClose,
  onItemsChanged,
}: {
  target: PhotosTarget | null;
  canEdit: boolean;
  onClose: () => void;
  onItemsChanged?: (items: Attachment[]) => void;
}) {
  const { items, busy, error, remove } = useAttachments(
    target?.owner ?? null,
    target?.ownerId ?? null,
    onItemsChanged,
  );

  const focusId = target?.focusAttachmentId ?? null;

  return (
    <AttachmentLightbox
      owner={target?.owner ?? null}
      ownerId={target?.ownerId ?? null}
      attachmentId={focusId}
      details={items.find((a) => a.id === focusId) ?? null}
      notice={error}
      canEdit={canEdit}
      busy={busy}
      // With no gallery behind it, what is left after a delete is shown by the
      // table row, which onItemsChanged has already updated.
      onDelete={async (id) => {
        if (await remove(id)) onClose();
      }}
      onClose={onClose}
    />
  );
}
