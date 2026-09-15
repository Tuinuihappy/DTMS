"use client";

import { ImageIcon, ImageOff, ImagePlus } from "lucide-react";
import { useState } from "react";
import { usePrefetchIntent } from "@/components/attachments/use-prefetch-intent";
import {
  attachmentImageUrl,
  attachmentThumbnailUrl,
  type AttachmentOwner,
} from "@/lib/api/fleet-attachments";

/**
 * A table cell showing an owner's newest photo, small, with a count when there
 * are more.
 *
 * The image address is stable (attachmentThumbnailUrl) and served with a
 * long-lived cache header, so after the first view a filter change or a page
 * flip costs no downloads at all. Loaded lazily, at low priority and at a fixed
 * size: a page of these must not crowd out the JSON calls that fill the table,
 * nor shift the rows around as they arrive.
 *
 * Key it on the cover as well as the row, so a broken-image state resets on its
 * own when the cover changes.
 */
export function PhotoCell({
  owner,
  ownerId,
  label,
  coverAttachmentId,
  photoCount,
  canEdit,
  onOpen,
}: {
  owner: AttachmentOwner;
  ownerId: string;
  /** Names the owner for screen readers, e.g. a carrier code. */
  label: string;
  coverAttachmentId: string | null;
  photoCount: number;
  canEdit: boolean;
  /** The cover's id to open straight onto it, or null for the gallery. */
  onOpen: (focusAttachmentId: string | null) => void;
}) {
  const [broken, setBroken] = useState(false);
  const cover = coverAttachmentId;
  // A click here opens the full picture; start fetching it while the pointer
  // is on its way.
  const prefetch = usePrefetchIntent(
    cover && !broken ? attachmentImageUrl(owner, ownerId, cover) : null,
  );

  if (!cover) {
    return canEdit ? (
      <button
        type="button"
        onClick={() => onOpen(null)}
        title="Add a photo"
        aria-label={`Add a photo of ${label}`}
        className="grid h-10 w-10 place-items-center rounded-[var(--radius-sm)] border border-dashed border-[var(--color-ink-200)] text-[var(--color-ink-400)] transition-colors hover:border-[var(--color-brand-500)] hover:text-[var(--color-brand-800)] dark:border-white/15"
      >
        <ImagePlus className="h-4 w-4" strokeWidth={2} />
      </button>
    ) : (
      <span className="grid h-10 w-10 place-items-center text-[var(--color-ink-300)]" aria-hidden>
        <ImageIcon className="h-4 w-4" strokeWidth={2} />
      </span>
    );
  }

  return (
    <button
      type="button"
      {...prefetch}
      // A broken image still opens: the dialog shows what is actually there,
      // which the cell cannot.
      onClick={() => onOpen(broken ? null : cover)}
      title={photoCount > 1 ? `${photoCount} photos` : "View photo"}
      aria-label={`Photos of ${label}`}
      className="group relative block h-10 w-10 overflow-hidden rounded-[var(--radius-sm)] border border-white/60 bg-[var(--color-ink-100)] transition-shadow hover:shadow-[0_8px_20px_-12px_rgba(15,23,42,0.55)] dark:border-white/10 dark:bg-white/[0.04]"
    >
      {broken ? (
        // A stable address does not expire, so a failure here means the image
        // is gone or no longer readable — not the "Expired" the gallery shows.
        <span className="grid h-full w-full place-items-center text-[var(--color-ink-400)]">
          <ImageOff className="h-4 w-4" strokeWidth={2} />
        </span>
      ) : (
        // eslint-disable-next-line @next/next/no-img-element -- served by our own
        // cache-controlled route; the Next optimiser would only add a hop
        <img
          src={attachmentThumbnailUrl(owner, ownerId, cover)}
          alt=""
          width={40}
          height={40}
          loading="lazy"
          decoding="async"
          fetchPriority="low"
          onError={() => setBroken(true)}
          className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.06]"
        />
      )}
      {photoCount > 1 && (
        <span className="absolute bottom-0.5 right-0.5 rounded-full bg-[var(--color-ink-900)]/80 px-1 text-[9px] font-semibold leading-[14px] text-white">
          {photoCount}
        </span>
      )}
    </button>
  );
}
