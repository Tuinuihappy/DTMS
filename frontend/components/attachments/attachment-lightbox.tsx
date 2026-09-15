"use client";

import { ImageOff, Loader2, Trash2, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  attachmentImageUrl,
  attachmentThumbnailUrl,
  type Attachment,
  type AttachmentOwner,
} from "@/lib/api/fleet-attachments";
import { aspectRatioOf, preloadImage } from "@/lib/image-preload";
import { cn } from "@/lib/utils";

// Matches MAX_DIM in lib/image-compress: no stored image is larger, so the frame
// never stretches one past its real size on a big screen.
const MAX_STORED_DIM = 1600;

// How long to hold out for the sharp picture before showing the thumbnail in
// its place. Inside this, opening looks like one step: the backdrop fades in and
// the finished photo arrives with it. Past it, a blurry preview that sharpens
// beats a dark screen with nothing on it.
const WAIT_FOR_SHARP_MS = 300;

// Only when even the preview is slow. A spinner that appears and vanishes
// within a frame or two is itself a flicker.
const SPINNER_DELAY_MS = 700;

/**
 * One photo, full size.
 *
 * Built to open and close without a flash:
 * - It is always mounted, backdrop included, so the backdrop fades in every
 *   time. A backdrop created in the same render that opens it starts at its
 *   end state and the page goes dark in one jump.
 * - Nothing is drawn until the picture's shape is known, so the frame appears
 *   once, at its final size, and the buttons under it never move.
 * - The sharp image is usually ready by the click, since hovering its thumbnail
 *   started the download (usePrefetchIntent). If not, it is given a moment
 *   before the thumbnail — cached, and the same shape because thumbnails are
 *   scaled, never cropped — stands in and the sharp one fades over it.
 *
 * Needs only the id to show the picture. `details` fills in the date, uploader
 * and size, and enables Delete, whenever the list catches up.
 */
export function AttachmentLightbox({
  owner,
  ownerId,
  attachmentId,
  details,
  notice,
  canEdit,
  busy,
  onDelete,
  onClose,
}: {
  /** Null while there is nothing to show; the lightbox stays mounted anyway. */
  owner: AttachmentOwner | null;
  ownerId: string | null;
  /** Open when set, together with the owner. */
  attachmentId: string | null;
  details: Attachment | null;
  /** Shown in place of the details, e.g. a failed delete. */
  notice?: string | null;
  canEdit: boolean;
  busy: boolean;
  onDelete: (id: string) => void;
  onClose: () => void;
}) {
  const open = owner !== null && ownerId !== null && attachmentId !== null;

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  return (
    <>
      <OverlayBackdrop
        open={open}
        onClick={onClose}
        className="z-[60] bg-[var(--color-ink-900)]/80 backdrop-blur-md"
      />
      <AnimatePresence>
        {owner && ownerId && attachmentId && (
          <div
            key="attachment-lightbox"
            className="pointer-events-none fixed inset-0 z-[61] flex items-center justify-center p-6"
          >
            {/* On close this element is kept, props frozen, while it animates
                out — which is why the owner may already be null by then. */}
            <LightboxBody
              key={attachmentId}
              thumbSrc={attachmentThumbnailUrl(owner, ownerId, attachmentId)}
              fullSrc={attachmentImageUrl(owner, ownerId, attachmentId)}
              attachmentId={attachmentId}
              details={details}
              notice={notice}
              canEdit={canEdit}
              busy={busy}
              onDelete={onDelete}
              onClose={onClose}
            />
          </div>
        )}
      </AnimatePresence>
    </>
  );
}

type LoadState = "loading" | "ready" | "broken";

type View =
  | { kind: "pending" }
  /** The sharp picture, shown from the first frame. */
  | { kind: "full"; ratio: number }
  /** The thumbnail standing in, and how the sharp one is getting on. */
  | { kind: "preview"; ratio: number; full: LoadState }
  | { kind: "missing" };

function LightboxBody({
  thumbSrc,
  fullSrc,
  attachmentId,
  details,
  notice,
  canEdit,
  busy,
  onDelete,
  onClose,
}: {
  thumbSrc: string;
  fullSrc: string;
  attachmentId: string;
  details: Attachment | null;
  notice?: string | null;
  canEdit: boolean;
  busy: boolean;
  onDelete: (id: string) => void;
  onClose: () => void;
}) {
  const [view, setView] = useState<View>({ kind: "pending" });
  const [slow, setSlow] = useState(false);
  const [confirming, setConfirming] = useState(false);

  // Decides what to draw first. Kept in plain locals rather than state: the
  // rules read several load outcomes at once, and only the result is rendered.
  useEffect(() => {
    let cancelled = false;
    let waited = false;
    let full: LoadState = "loading";
    let fullRatio = 1;
    let thumb: LoadState | "idle" = "idle";
    let thumbRatio = 1;
    let shown: View["kind"] = "pending";

    const show = (next: View) => {
      shown = next.kind;
      setView(next);
    };

    const decide = () => {
      if (cancelled) return;

      if (shown === "preview") {
        show({ kind: "preview", ratio: thumbRatio, full });
        return;
      }
      if (shown !== "pending") return;

      if (full === "ready") return show({ kind: "full", ratio: fullRatio });
      if (full === "loading" && !waited) return;

      // The sharp picture is late or failed: fall back to the thumbnail.
      if (thumb === "idle") {
        thumb = "loading";
        preloadImage(thumbSrc).then(
          (img) => {
            thumb = "ready";
            thumbRatio = aspectRatioOf(img);
            decide();
          },
          () => {
            thumb = "broken";
            decide();
          },
        );
        return;
      }
      if (thumb === "ready") return show({ kind: "preview", ratio: thumbRatio, full });
      if (thumb === "broken" && full === "broken") show({ kind: "missing" });
      // Otherwise the thumbnail is still loading, or failed while the sharp
      // picture may yet arrive; the next outcome decides.
    };

    preloadImage(fullSrc).then(
      (img) => {
        full = "ready";
        fullRatio = aspectRatioOf(img);
        decide();
      },
      () => {
        full = "broken";
        decide();
      },
    );

    const waitTimer = window.setTimeout(() => {
      waited = true;
      decide();
    }, WAIT_FOR_SHARP_MS);
    const spinnerTimer = window.setTimeout(() => {
      if (!cancelled && shown === "pending") setSlow(true);
    }, SPINNER_DELAY_MS);

    return () => {
      cancelled = true;
      window.clearTimeout(waitTimer);
      window.clearTimeout(spinnerTimer);
    };
  }, [thumbSrc, fullSrc]);

  // Nothing is mounted until there is something final to show, so the entrance
  // animation runs once, on the finished frame.
  if (view.kind === "pending" && !slow) return null;

  const ratio = view.kind === "full" || view.kind === "preview" ? view.ratio : null;
  const fullBroken = view.kind === "preview" && view.full === "broken";

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.96 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.97, transition: { duration: 0.15 } }}
      transition={{ type: "spring", stiffness: 340, damping: 30 }}
      className="pointer-events-auto flex max-h-full flex-col items-center gap-3"
    >
      {ratio !== null ? (
        <div
          className="relative overflow-hidden rounded-[var(--radius-lg)] bg-white/5 shadow-2xl"
          style={{
            aspectRatio: String(ratio),
            width: `min(calc(100vw - 3rem), calc(74vh * ${ratio}), ${Math.round(MAX_STORED_DIM * Math.min(1, ratio))}px)`,
          }}
        >
          {view.kind === "full" ? (
            // Already decoded; sync decoding paints it on this very frame.
            // eslint-disable-next-line @next/next/no-img-element -- stable cached route
            <img
              src={fullSrc}
              alt={details?.caption ?? "Attachment"}
              decoding="sync"
              className="absolute inset-0 h-full w-full object-cover"
            />
          ) : (
            <>
              {/* eslint-disable-next-line @next/next/no-img-element -- stable cached route */}
              <img
                src={thumbSrc}
                alt=""
                aria-hidden
                decoding="sync"
                className="absolute inset-0 h-full w-full object-cover"
              />
              {view.kind === "preview" && view.full === "ready" && (
                <motion.img
                  src={fullSrc}
                  alt={details?.caption ?? "Attachment"}
                  decoding="sync"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ duration: 0.2 }}
                  className="absolute inset-0 h-full w-full object-cover"
                />
              )}
            </>
          )}
        </div>
      ) : view.kind === "missing" ? (
        <div className="flex h-40 w-64 flex-col items-center justify-center gap-2 rounded-[var(--radius-lg)] bg-white/10 text-[12px] font-medium text-white/80">
          <ImageOff className="h-5 w-5" strokeWidth={2} />
          This photo is no longer available.
        </div>
      ) : (
        <div className="grid h-40 w-64 place-items-center">
          <Loader2 className="h-6 w-6 animate-spin text-white/70" strokeWidth={2.2} />
        </div>
      )}

      {/* A fixed-height line, filled when the details arrive, so the buttons
          below never move. */}
      <div className="flex h-4 items-center gap-3 text-[11.5px] text-white/75">
        {notice ? (
          <span className="font-medium text-[var(--color-coral-soft)]">{notice}</span>
        ) : fullBroken ? (
          <span>Full-size image unavailable — showing the preview.</span>
        ) : details ? (
          <>
            <span>
              {new Date(details.uploadedAt).toLocaleString()} · {details.uploadedBy}
            </span>
            <span>{Math.round(details.sizeBytes / 1024).toLocaleString()} KB</span>
          </>
        ) : null}
      </div>

      <div className="flex items-center gap-2">
        {canEdit &&
          (confirming ? (
            <>
              <span className="text-[11.5px] font-medium text-white/85">Delete permanently?</span>
              <button
                type="button"
                disabled={busy}
                onClick={() => onDelete(attachmentId)}
                className={cn(
                  "inline-flex items-center gap-1.5 rounded-full bg-[var(--color-coral)] px-3.5 py-1.5 text-[11.5px] font-semibold text-white",
                  busy && "opacity-60",
                )}
              >
                {busy && <Loader2 className="h-3 w-3 animate-spin" strokeWidth={2.4} />}
                Delete
              </button>
              <button
                type="button"
                onClick={() => setConfirming(false)}
                className="rounded-full bg-white/15 px-3.5 py-1.5 text-[11.5px] font-semibold text-white/90 hover:bg-white/25"
              >
                Keep
              </button>
            </>
          ) : (
            <button
              type="button"
              // Until the list confirms the image is there, there is nothing
              // to delete — and nothing to hand the table afterwards.
              disabled={!details}
              onClick={() => setConfirming(true)}
              className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3.5 py-1.5 text-[11.5px] font-semibold text-white/90 transition-colors hover:bg-white/25 disabled:opacity-50"
            >
              <Trash2 className="h-3.5 w-3.5" strokeWidth={2.2} />
              Delete
            </button>
          ))}

        <button
          type="button"
          onClick={onClose}
          className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3.5 py-1.5 text-[11.5px] font-semibold text-white/90 transition-colors hover:bg-white/25"
        >
          <X className="h-3.5 w-3.5" strokeWidth={2.4} />
          Close
        </button>
      </div>
    </motion.div>
  );
}
