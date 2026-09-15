"use client";

// Fetch and decode an image before anything shows it, so the <img> that later
// uses the same address paints complete on its first frame instead of blank or
// half-drawn.
//
// Memoised by address: a hover that starts a download and the click that
// follows share one request, and the view that opens can wait on the very
// promise the hover began. Bounded, because each entry holds an image element
// and with it a decoded bitmap — a full photo is several MB of pixels.

const MAX_ENTRIES = 24;

const cache = new Map<string, Promise<HTMLImageElement>>();

export function preloadImage(src: string): Promise<HTMLImageElement> {
  const hit = cache.get(src);
  if (hit) {
    // Most recently used goes last, so the oldest is the one evicted.
    cache.delete(src);
    cache.set(src, hit);
    return hit;
  }

  const img = new Image();
  img.decoding = "async";
  img.src = src;
  const promise = img.decode().then(() => img);

  // A failure is not remembered: the next attempt should really try again.
  // This handler also keeps a fire-and-forget preload from surfacing as an
  // unhandled rejection.
  promise.catch(() => {
    if (cache.get(src) === promise) cache.delete(src);
  });

  cache.set(src, promise);
  while (cache.size > MAX_ENTRIES) {
    const oldest = cache.keys().next().value;
    if (oldest === undefined) break;
    cache.delete(oldest);
  }
  return promise;
}

/** Width over height, never zero or NaN. */
export const aspectRatioOf = (img: HTMLImageElement) =>
  img.naturalWidth > 0 && img.naturalHeight > 0 ? img.naturalWidth / img.naturalHeight : 1;
