"use client";

// Client-side downscale + JPEG re-encode. Phone cameras hand us 2-3 MiB
// originals at 12-50 MP, which is far more than anything here renders and slow
// to upload on cellular. Resizing the long edge and re-encoding lands a photo
// around 300-600 KiB while keeping faces, license plates, and signatures
// legible.
//
// All work happens on a <canvas> in the browser, so the original file never
// leaves the device unaltered.

const MAX_DIM = 1600;
const QUALITY = 0.8;

// A grid of 600 KiB photos is a slow page for no reason: a maintenance panel
// with five episodes and three photos each would pull ~9 MB to show thumbnails
// nobody has clicked yet. Drawing a second, small copy costs almost nothing
// because the source is already decoded, and it is ~30x less to transfer.
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
  const bitmap = await decode(file);
  if (!bitmap) return file;

  try {
    return (await render(bitmap, MAX_DIM, QUALITY, file)) ?? file;
  } finally {
    bitmap.close?.();
  }
}

/** Full size plus a thumbnail, from a single decode of the source. */
export async function compressImagePair(file: File): Promise<CompressedImage> {
  const bitmap = await decode(file);
  if (!bitmap) return { full: file, thumbnail: null };

  try {
    const full = (await render(bitmap, MAX_DIM, QUALITY, file)) ?? file;
    const thumbnail = await render(bitmap, THUMB_DIM, THUMB_QUALITY, null);
    return { full, thumbnail };
  } finally {
    bitmap.close?.();
  }
}

async function decode(file: File): Promise<ImageBitmap | null> {
  if (!file.type.startsWith("image/")) return null;
  try {
    return await createImageBitmap(file);
  } catch {
    return null;
  }
}

/**
 * Draws the bitmap down to `maxDim` on its long edge and re-encodes as JPEG.
 *
 * `original` is the yardstick for "did this actually help": when a re-encode
 * comes out no smaller than the source, the source is better. A thumbnail has
 * no such yardstick — it is wanted at that size regardless — so it passes null.
 */
async function render(
  bitmap: ImageBitmap,
  maxDim: number,
  quality: number,
  original: File | null,
): Promise<Blob | null> {
  const scale = Math.min(1, maxDim / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.max(1, Math.round(bitmap.width * scale));
  canvas.height = Math.max(1, Math.round(bitmap.height * scale));

  const ctx = canvas.getContext("2d");
  if (!ctx) return null;
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);

  const blob = await new Promise<Blob | null>((resolve) =>
    canvas.toBlob(resolve, "image/jpeg", quality),
  );
  if (!blob) return null;
  if (original && blob.size >= original.size) return null;
  return blob;
}
