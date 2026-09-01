"use client";

import { ImageIcon, ImageOff, Loader2, Trash2, Upload, X } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useCallback, useEffect, useRef, useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import {
  deleteAttachment,
  listAttachments,
  uploadAttachment,
  type Attachment,
  type AttachmentOwner,
} from "@/lib/api/fleet-attachments";
import { cn } from "@/lib/utils";

// Mirrors the server's allow-list. Not "image/*": that admits SVG, which the
// server refuses anyway — better to grey it out in the picker than to let
// someone choose a file and be told no afterwards.
const ACCEPT = "image/jpeg,image/png,image/webp";

export function AttachmentGallery({
  owner,
  ownerId,
  canEdit,
  emptyHint,
}: {
  owner: AttachmentOwner;
  ownerId: string;
  canEdit: boolean;
  emptyHint?: string;
}) {
  const [items, setItems] = useState<Attachment[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [zoomed, setZoomed] = useState<Attachment | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const refresh = useCallback(
    (signal?: AbortSignal) =>
      listAttachments(owner, ownerId, signal)
        .then(setItems)
        .catch((e: Error) => {
          if (e.name !== "AbortError") setError(e.message);
        })
        .finally(() => {
          if (!signal?.aborted) setLoading(false);
        }),
    [owner, ownerId],
  );

  useEffect(() => {
    const ac = new AbortController();
    setLoading(true);
    void refresh(ac.signal);
    return () => ac.abort();
  }, [refresh]);

  const onPick = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? []);
    if (files.length === 0) return;

    setBusy(true);
    setError(null);
    try {
      // Sequential, not parallel. Each upload is three round trips plus the
      // bytes, and the per-owner ceiling is checked server-side per call —
      // firing them at once would race that check and flood a phone's uplink.
      for (const file of files) {
        await uploadAttachment(owner, ownerId, file);
      }
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.");
      // Some may have succeeded before the failure; show what actually landed.
      await refresh();
    } finally {
      setBusy(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  };

  const onDelete = async (a: Attachment) => {
    setBusy(true);
    setError(null);
    try {
      await deleteAttachment(a.id);
      setItems((prev) => prev.filter((x) => x.id !== a.id));
      setZoomed(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete the image.");
    } finally {
      setBusy(false);
    }
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
              {busy ? "Uploading…" : "Add"}
            </button>
          </>
        )}
      </div>

      {error && (
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
              <Thumb attachment={a} onZoom={() => setZoomed(a)} />
            </li>
          ))}
        </ul>
      )}

      <Lightbox
        attachment={zoomed}
        canEdit={canEdit}
        busy={busy}
        onDelete={onDelete}
        onClose={() => setZoomed(null)}
      />
    </div>
  );
}

function Thumb({ attachment, onZoom }: { attachment: Attachment; onZoom: () => void }) {
  const [broken, setBroken] = useState(false);

  // Prefer the small copy — a grid of full-size photos is megabytes for
  // pictures nobody has clicked yet. Falls back when a thumbnail could not be
  // produced at upload time.
  const src = attachment.thumbnailUrl ?? attachment.url;

  return (
    <button
      type="button"
      onClick={onZoom}
      disabled={broken}
      title={attachment.caption ?? attachment.originalFileName ?? undefined}
      className="group relative h-[84px] w-[84px] overflow-hidden rounded-[var(--radius-lg)] border border-white/50 bg-[var(--color-ink-100)] transition-shadow hover:shadow-[0_10px_24px_-14px_rgba(15,23,42,0.5)] disabled:cursor-default dark:border-white/10 dark:bg-white/[0.04]"
    >
      {broken ? (
        // These URLs expire, and the object can be removed out from under one.
        // Naming that beats a silently blank square.
        <span className="flex h-full w-full flex-col items-center justify-center gap-1 px-1.5 text-center text-[9.5px] font-medium text-[var(--color-ink-500)]">
          <ImageOff className="h-3.5 w-3.5" strokeWidth={2} />
          Expired
        </span>
      ) : (
        // eslint-disable-next-line @next/next/no-img-element -- signed storage
        // URL, deliberately outside the Next image optimiser
        <img
          src={src}
          alt={attachment.caption ?? "Attachment"}
          loading="lazy"
          onError={() => setBroken(true)}
          className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.05]"
        />
      )}
    </button>
  );
}

function Lightbox({
  attachment,
  canEdit,
  busy,
  onDelete,
  onClose,
}: {
  attachment: Attachment | null;
  canEdit: boolean;
  busy: boolean;
  onDelete: (a: Attachment) => void;
  onClose: () => void;
}) {
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    setConfirming(false);
  }, [attachment]);

  useEffect(() => {
    if (!attachment) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [attachment, onClose]);

  return (
    <>
      <OverlayBackdrop
        open={attachment !== null}
        onClick={onClose}
        className="z-[60] bg-[var(--color-ink-900)]/80 backdrop-blur-md"
      />
      <AnimatePresence>
        {attachment && (
          <div
            key="attachment-lightbox"
            className="pointer-events-none fixed inset-0 z-[61] flex items-center justify-center p-6"
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.97, transition: { duration: 0.15 } }}
              transition={{ type: "spring", stiffness: 340, damping: 30 }}
              className="pointer-events-auto flex max-h-full flex-col items-center gap-3"
            >
              {/* eslint-disable-next-line @next/next/no-img-element -- signed storage URL */}
              <img
                src={attachment.url}
                alt={attachment.caption ?? "Attachment"}
                className="max-h-[74vh] max-w-full rounded-[var(--radius-lg)] object-contain shadow-2xl"
              />

              <div className="flex items-center gap-3 text-[11.5px] text-white/75">
                <span>
                  {new Date(attachment.uploadedAt).toLocaleString()} · {attachment.uploadedBy}
                </span>
                <span>{Math.round(attachment.sizeBytes / 1024).toLocaleString()} KB</span>
              </div>

              <div className="flex items-center gap-2">
                {canEdit &&
                  (confirming ? (
                    <>
                      <span className="text-[11.5px] font-medium text-white/85">
                        Delete permanently?
                      </span>
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => onDelete(attachment)}
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
                      onClick={() => setConfirming(true)}
                      className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3.5 py-1.5 text-[11.5px] font-semibold text-white/90 transition-colors hover:bg-white/25"
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
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
