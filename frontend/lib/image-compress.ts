"use client";

// Client-side downscale + JPEG re-encode. Phone cameras hand us 2-3 MiB
// originals at 12-50 MP, which is far more than anything here renders and slow
// to upload on cellular. Resizing the long edge and re-encoding keeps faces,
// license plates, and signatures legible at a fraction of the bytes.
//
// All work happens on a canvas in the browser, so the original file never
// leaves the device unaltered.

const MAX_DIM = 1600;
const QUALITY = 0.8;

// A grid of full-size photos is a slow page for no reason: a maintenance panel
// with five episodes and three photos each would pull megabytes to show
// thumbnails nobody has clicked yet.
const THUMB_DIM = 320;
const THUMB_QUALITY = 0.72;

export type CompressedImage = {
  full: Blob;
  /** Null when the browser could not produce one. Galleries fall back to the
   *  full image rather than showing a gap. */
  thumbnail: Blob | null;
};

/**
 * Full-size only. Kept for the POD capture flow, which uploads one image.
 *
 * Returns the untouched file when the browser cannot decode it (non-image,
 * older HEIC on some Android builds) so the caller still has something to work
 * with — which is exactly why the upload ceiling has to live on the server:
 * this path can hand back a 50 MB original.
 */
export async function compressImage(file: File): Promise<Blob> {
  const started = now();
  const bitmap = await decode(file);
  if (!bitmap) return file;
  const decoded = now();

  let surface: Surface | null = null;
  try {
    surface = draw(bitmap, bitmap.width, bitmap.height, MAX_DIM);
  } finally {
    bitmap.close?.();
  }
  if (!surface) return file;
  const drawn = now();

  const blob = await encode(surface, QUALITY);
  report(file, decoded - started, drawn - decoded, now() - drawn);
  return blob && blob.size < file.size ? blob : file;
}

/** Full size plus a thumbnail, from a single decode of the source. */
export async function compressImagePair(file: File): Promise<CompressedImage> {
  const started = now();
  const bitmap = await decode(file);
  if (!bitmap) return { full: file, thumbnail: null };
  const decoded = now();

  let fullSurface: Surface | null = null;
  try {
    fullSurface = draw(bitmap, bitmap.width, bitmap.height, MAX_DIM);
  } finally {
    // Released before either encode rather than after both. A 12 MP source is
    // ~48 MB of pixels; holding it through the encodes is the peak this whole
    // function is measured by on a tablet.
    bitmap.close?.();
  }
  if (!fullSurface) return { full: file, thumbnail: null };
  const drawn = now();

  const fullBlob = await encode(fullSurface, QUALITY);
  const full = fullBlob && fullBlob.size < file.size ? fullBlob : file;

  // Drawn from the already-downscaled copy, not the source. Going straight from
  // 12 MP to 320px means resampling ~50x more pixels than going from 1600px,
  // for an image whose whole job is to be small; the extra step also aliases
  // less, so it is not a quality trade.
  const thumbSurface = draw(fullSurface, fullSurface.width, fullSurface.height, THUMB_DIM);
  const thumbnail = thumbSurface ? await encode(thumbSurface, THUMB_QUALITY) : null;

  report(file, decoded - started, drawn - decoded, now() - drawn);
  return { full, thumbnail };
}

// ── Canvas plumbing ──────────────────────────────────────────────────

type Surface = OffscreenCanvas | HTMLCanvasElement;

async function decode(file: File): Promise<ImageBitmap | null> {
  if (!file.type.startsWith("image/")) return null;
  try {
    return await createImageBitmap(file);
  } catch {
    return null;
  }
}

/** Draws a source down to `maxDim` on its long edge. Never enlarges. */
function draw(
  source: CanvasImageSource,
  sourceWidth: number,
  sourceHeight: number,
  maxDim: number,
): Surface | null {
  const scale = Math.min(1, maxDim / Math.max(sourceWidth, sourceHeight));
  const width = Math.max(1, Math.round(sourceWidth * scale));
  const height = Math.max(1, Math.round(sourceHeight * scale));

  const surface = createSurface(width, height);
  const ctx = surface.getContext("2d") as
    | CanvasRenderingContext2D
    | OffscreenCanvasRenderingContext2D
    | null;
  if (!ctx) return null;

  ctx.imageSmoothingQuality = "high";
  ctx.drawImage(source, 0, 0, width, height);
  return surface;
}

function createSurface(width: number, height: number): Surface {
  // OffscreenCanvas encodes through convertToBlob(), which browsers run off the
  // main thread — the difference between a tablet that redraws while a photo is
  // being prepared and one that appears frozen.
  if (typeof OffscreenCanvas !== "undefined") return new OffscreenCanvas(width, height);
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  return canvas;
}

async function encode(surface: Surface, quality: number): Promise<Blob | null> {
  try {
    if ("convertToBlob" in surface) {
      return await surface.convertToBlob({ type: "image/jpeg", quality });
    }
    return await new Promise<Blob | null>((resolve) =>
      surface.toBlob(resolve, "image/jpeg", quality),
    );
  } catch {
    return null;
  }
}

// ── Timing ───────────────────────────────────────────────────────────

const now = () =>
  typeof performance !== "undefined" ? performance.now() : Date.now();

/**
 * Preparing a photo happens before any request exists, so a slow decode looks
 * identical to a slow upload in DevTools — the Network panel is simply empty
 * while it runs. These numbers are the only way to tell the two apart from a
 * real device.
 */
function report(file: File, decodeMs: number, drawMs: number, encodeMs: number): void {
  console.debug(
    `[image-compress] ${(file.size / 1024).toFixed(0)} KiB ${file.type} — ` +
      `decode ${decodeMs.toFixed(0)}ms, draw ${drawMs.toFixed(0)}ms, encode ${encodeMs.toFixed(0)}ms`,
  );
}
