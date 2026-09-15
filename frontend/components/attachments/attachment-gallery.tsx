"use client";

import { ImageIcon, ImageOff, Loader2, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { AttachmentLightbox } from "@/components/attachments/attachment-lightbox";
import { useAttachments } from "@/components/attachments/use-attachments";
import { usePrefetchIntent } from "@/components/attachments/use-prefetch-intent";
import {
  attachmentImageUrl,
  attachmentThumbnailUrl,
  type Attachment,
  type AttachmentOwner,
} from "@/lib/api/fleet-attachments";

// Mirrors the server's allow-list. Not "image/*": that admits SVG, which the
// server refuses anyway — better to grey it out in the picker than to let
// someone choose a file and be told no afterwards.
const ACCEPT = "image/jpeg,image/png,image/webp";

export function AttachmentGallery({
  owner,
  ownerId,
  canEdit,
  emptyHint,
  onItemsChanged,
}: {
  owner: AttachmentOwner;
  ownerId: string;
  canEdit: boolean;
  emptyHint?: string;
  /** See useAttachments. */
  onItemsChanged?: (items: Attachment[]) => void;
}) {
  const { items, loading, busy, phase, error, upload, remove } = useAttachments(
    owner,
    ownerId,
    onItemsChanged,
  );
  const [zoomedId, setZoomedId] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const onPick = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    await upload(files);
    if (inputRef.current) inputRef.current.value = "";
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2">
        <h3 className="flex flex-1 items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
          <ImageIcon className="h-3.5 w-3.5" strokeWidth={2.2} />
          Photos {items.length > 0 && `(${items.length})`}
        </h3>

        {canEdit && (
          <>
            <input
              ref={inputRef}
              type="file"
              accept={ACCEPT}
              // Opens the rear camera on a phone and the file picker on a
              // desktop — one control serving both entry points.
              capture="environment"
              multiple
              onChange={onPick}
              className="hidden"
            />
            <button
              type="button"
              onClick={() => inputRef.current?.click()}
              disabled={busy}
              className="inline-flex h-7 items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-3 text-[11px] font-semibold text-white transition-all hover:shadow-[0_10px_24px_-12px_rgba(15,23,42,0.5)] disabled:opacity-50 dark:bg-[var(--color-brand-500)]"
            >
              {busy ? (
                <Loader2 className="h-3 w-3 animate-spin" strokeWidth={2.4} />
              ) : (
                <Upload className="h-3 w-3" strokeWidth={2.4} />
              )}
              {phase === "preparing" ? "Preparing…" : busy ? "Uploading…" : "Add"}
            </button>
          </>
        )}
      </div>

      {/* The lightbox shows its own errors while it is open. */}
      {error && !zoomedId && (
        <div className="rounded-md bg-[var(--color-coral-soft)] px-3 py-2 text-[11.5px] font-medium text-[var(--color-coral)]">
          {error}
        </div>
      )}

      {loading ? (
        <div className="grid place-items-center py-6">
          <Loader2 className="h-5 w-5 animate-spin text-[var(--color-ink-400)]" strokeWidth={2.2} />
        </div>
      ) : items.length === 0 ? (
        <p className="text-[11.5px] text-[var(--color-ink-500)]">
          {emptyHint ?? "No photos yet."}
        </p>
      ) : (
        <ul className="flex flex-wrap gap-2.5">
          {items.map((a) => (
            <li key={a.id}>
              <Thumb
                src={attachmentThumbnailUrl(owner, ownerId, a.id)}
                fullSrc={attachmentImageUrl(owner, ownerId, a.id)}
                attachment={a}
                onZoom={() => setZoomedId(a.id)}
              />
            </li>
          ))}
        </ul>
      )}

      <AttachmentLightbox
        owner={owner}
        ownerId={ownerId}
        attachmentId={zoomedId}
        details={items.find((a) => a.id === zoomedId) ?? null}
        notice={error}
        canEdit={canEdit}
        busy={busy}
        onDelete={async (id) => {
          if (await remove(id)) setZoomedId(null);
        }}
        onClose={() => setZoomedId(null)}
      />
    </div>
  );
}

function Thumb({
  src,
  fullSrc,
  attachment,
  onZoom,
}: {
  src: string;
  /** Fetched on hover, so the lightbox opens onto a sharp picture. */
  fullSrc: string;
  attachment: Attachment;
  onZoom: () => void;
}) {
  const [broken, setBroken] = useState(false);
  const prefetch = usePrefetchIntent(broken ? null : fullSrc);

  // The stable thumbnail address rather than the list's signed URL: the signed
  // one changes on every list call, so the browser could never reuse it, and a
  // photo the table already showed would download again here.
  return (
    <button
      type="button"
      {...prefetch}
      onClick={onZoom}
      disabled={broken}
      title={attachment.caption ?? attachment.originalFileName ?? undefined}
      className="group relative h-[84px] w-[84px] overflow-hidden rounded-[var(--radius-lg)] border border-white/50 bg-[var(--color-ink-100)] transition-shadow hover:shadow-[0_10px_24px_-14px_rgba(15,23,42,0.5)] disabled:cursor-default dark:border-white/10 dark:bg-white/[0.04]"
    >
      {broken ? (
        // A stable address does not expire, so a failure means the image is
        // gone or unreadable. Naming that beats a silently blank square.
        <span className="flex h-full w-full flex-col items-center justify-center gap-1 px-1.5 text-center text-[9.5px] font-medium text-[var(--color-ink-500)]">
          <ImageOff className="h-3.5 w-3.5" strokeWidth={2} />
          Unavailable
        </span>
      ) : (
        // eslint-disable-next-line @next/next/no-img-element -- served by our own
        // cache-controlled route; the Next optimiser would only add a hop
        <img
          src={src}
          alt={attachment.caption ?? "Attachment"}
          loading="lazy"
          decoding="async"
          onError={() => setBroken(true)}
          className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.05]"
        />
      )}
    </button>
  );
}
