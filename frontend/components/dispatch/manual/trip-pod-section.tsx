"use client";

import { Camera, ImageOff, Loader2 } from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useEffect, useState } from "react";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { getTripPodPhotos, type TripPodPhoto } from "@/lib/api/admin-manual";

/**
 * Proof-of-delivery photos on the dispatcher's trip drawer.
 *
 * Operators have been taking these since July and nobody has been able to
 * look at one: the keys were written and read by nothing, and the bucket is
 * private. This is the first place they surface.
 *
 * Renders nothing at all when a trip has no photos — which is every AMR trip,
 * and any Manual trip where the operator skipped the (optional) capture.
 */
export function TripPodSection({ tripId }: { tripId: string }) {
  const [pickup, setPickup] = useState<TripPodPhoto | null>(null);
  const [drop, setDrop] = useState<TripPodPhoto | null>(null);
  const [loading, setLoading] = useState(true);
  const [zoomed, setZoomed] = useState<{ url: string; label: string } | null>(null);

  useEffect(() => {
    const ac = new AbortController();
    setLoading(true);
    getTripPodPhotos(tripId)
      .then((res) => {
        if (ac.signal.aborted) return;
        setPickup(res.pickup);
        setDrop(res.drop);
      })
      .catch(() => {
        // A trip with no photos is the common case and is not worth an error
        // banner in a drawer full of more important things.
      })
      .finally(() => {
        if (!ac.signal.aborted) setLoading(false);
      });
    return () => ac.abort();
  }, [tripId]);

  if (loading || (!pickup && !drop)) return null;

  return (
    <section>
      <h3 className="mb-2 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-400)]">
        <Camera className="h-3.5 w-3.5" strokeWidth={2.2} />
        Proof of delivery
      </h3>

      <div className="flex flex-wrap gap-3">
        <Thumb photo={pickup} label="Pickup" onZoom={setZoomed} />
        <Thumb photo={drop} label="Drop" onZoom={setZoomed} />
      </div>

      <Lightbox zoomed={zoomed} onClose={() => setZoomed(null)} />
    </section>
  );
}

function Thumb({
  photo,
  label,
  onZoom,
}: {
  photo: TripPodPhoto | null;
  label: string;
  onZoom: (v: { url: string; label: string }) => void;
}) {
  const [broken, setBroken] = useState(false);

  if (!photo) {
    return (
      <div className="flex h-[104px] w-[104px] flex-col items-center justify-center gap-1 rounded-[var(--radius-lg)] border border-dashed border-white/50 text-[var(--color-ink-400)] dark:border-white/10">
        <ImageOff className="h-4 w-4" strokeWidth={2} />
        <span className="text-[10px] font-medium">No {label.toLowerCase()}</span>
      </div>
    );
  }

  return (
    <figure className="flex flex-col gap-1">
      <button
        type="button"
        onClick={() => !broken && onZoom({ url: photo.url, label })}
        className="group relative h-[104px] w-[104px] overflow-hidden rounded-[var(--radius-lg)] border border-white/50 bg-[var(--color-ink-100)] transition-shadow hover:shadow-[0_10px_28px_-14px_rgba(15,23,42,0.55)] dark:border-white/10 dark:bg-white/[0.04]"
        aria-label={`Enlarge ${label.toLowerCase()} photo`}
      >
        {broken ? (
          // The URL is time-limited and the object can be removed out from
          // under it, so a dead image is a state worth naming rather than a
          // silently blank square.
          <span className="flex h-full w-full flex-col items-center justify-center gap-1 px-2 text-center text-[10px] font-medium text-[var(--color-ink-500)]">
            <ImageOff className="h-4 w-4" strokeWidth={2} />
            Link expired — reopen
          </span>
        ) : (
          // eslint-disable-next-line @next/next/no-img-element -- signed
          // storage URL, deliberately outside the Next image optimiser
          <img
            src={photo.url}
            alt={`${label} proof of delivery`}
            loading="lazy"
            onError={() => setBroken(true)}
            className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.04]"
          />
        )}
      </button>
      <figcaption className="text-[10.5px] font-medium text-[var(--color-ink-500)]">
        {label}
      </figcaption>
    </figure>
  );
}

function Lightbox({
  zoomed,
  onClose,
}: {
  zoomed: { url: string; label: string } | null;
  onClose: () => void;
}) {
  useEffect(() => {
    if (!zoomed) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [zoomed, onClose]);

  return (
    <>
      <OverlayBackdrop
        open={zoomed !== null}
        onClick={onClose}
        className="z-[60] bg-[var(--color-ink-900)]/80 backdrop-blur-md"
      />
      <AnimatePresence>
        {zoomed && (
          <div
            key="pod-lightbox"
            className="pointer-events-none fixed inset-0 z-[61] flex items-center justify-center p-6"
          >
            <motion.figure
              initial={{ opacity: 0, scale: 0.95 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.97, transition: { duration: 0.15 } }}
              transition={{ type: "spring", stiffness: 340, damping: 30 }}
              className="pointer-events-auto flex max-h-full flex-col items-center gap-2"
            >
              {/* eslint-disable-next-line @next/next/no-img-element -- signed storage URL */}
              <img
                src={zoomed.url}
                alt={`${zoomed.label} proof of delivery, enlarged`}
                className="max-h-[80vh] max-w-full rounded-[var(--radius-lg)] object-contain shadow-2xl"
              />
              <figcaption className="text-[12px] font-medium text-white/80">
                {zoomed.label} · click anywhere to close
              </figcaption>
            </motion.figure>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
