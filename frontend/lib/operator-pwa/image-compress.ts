"use client";

// Client-side downscale + JPEG re-encode for POD photos. Phone cameras
// hand us 2-3 MiB originals at 12-50 MP, which is far more than POD
// needs (the dashboard renders them at ~600px) and slow to upload on
// cellular. We resize the long edge to MAX_DIM and re-encode at QUALITY
// to land each photo around 300-600 KiB while keeping faces, license
// plates, and signatures legible.
//
// All work happens on a <canvas> in the browser so the original file
// never leaves the device unaltered. Falls back to the untouched file
// if the browser can't decode the input (rare — non-image, oversized
// HEIC on old Android, etc.) so the user still gets a successful upload.

const MAX_DIM = 1600;
const QUALITY = 0.8;

export async function compressImage(file: File): Promise<Blob> {
  if (!file.type.startsWith("image/")) return file;

  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file);
  } catch {
    return file;
  }

  const { width, height } = bitmap;
  const scale = Math.min(1, MAX_DIM / Math.max(width, height));
  const targetW = Math.round(width * scale);
  const targetH = Math.round(height * scale);

  const canvas = document.createElement("canvas");
  canvas.width = targetW;
  canvas.height = targetH;
  const ctx = canvas.getContext("2d");
  if (!ctx) return file;
  ctx.drawImage(bitmap, 0, 0, targetW, targetH);
  bitmap.close?.();

  const blob = await new Promise<Blob | null>((resolve) =>
    canvas.toBlob(resolve, "image/jpeg", QUALITY),
  );
  if (!blob) return file;
  if (blob.size >= file.size) return file;
  return blob;
}
