"use client";

// Client for image attachments (ADR-019). Bytes go browser-to-storage; only
// the small JSON calls pass through DTMS.

import { compressImagePair } from "@/lib/image-compress";

export type AttachmentOwner = "carrier" | "carrier-type" | "maintenance";

/** Preparing happens before any request exists, so without this the UI would
 *  claim it was uploading while the browser was still resizing a 12 MP photo —
 *  the phase a user is most likely to be waiting on, and the one DevTools shows
 *  as a gap with nothing in it. */
export type UploadPhase = "preparing" | "uploading";

export type Attachment = {
  id: string;
  /** Time-limited. Regenerated with a fresh timestamp on every list call, so a
   *  browser treats each one as a new resource — hold them rather than
   *  re-fetching per render. */
  url: string;
  thumbnailUrl: string | null;
  contentType: string;
  sizeBytes: number;
  originalFileName: string | null;
  caption: string | null;
  uploadedAt: string;
  uploadedBy: string;
  expiresAt: string;
};

type PresignedTarget = {
  url: string;
  fields: Record<string, string>;
  objectKey: string;
  expiresAt: string;
};

type PresignResponse = {
  uploadId: string;
  image: PresignedTarget;
  thumbnail: PresignedTarget | null;
};

async function send<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, { credentials: "include", cache: "no-store", ...init });
  if (!res.ok) {
    // Surface the backend's own words — a refusal usually says exactly what is
    // wrong ("already has 10 images, the maximum is 10").
    const body = await res.text();
    throw new Error(body?.replace(/^"|"$/g, "") || `Request failed (${res.status}).`);
  }
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
}

export const listAttachments = (owner: AttachmentOwner, ownerId: string, signal?: AbortSignal) =>
  send<Attachment[]>(
    `/api/fleet/attachments?owner=${owner}&ownerId=${encodeURIComponent(ownerId)}`,
    { signal },
  );

export const deleteAttachment = (id: string) =>
  send<void>(`/api/fleet/attachments/${encodeURIComponent(id)}`, { method: "DELETE" });

/**
 * Compress, upload, then record.
 *
 * The record is written last on purpose: a row created before the upload would
 * be stranded the moment someone closed the tab, whereas one created after
 * exists only for bytes that actually arrived. Bytes uploaded but never
 * confirmed sit in a staging prefix that expires on its own.
 */
export async function uploadAttachment(
  owner: AttachmentOwner,
  ownerId: string,
  file: File,
  caption?: string | null,
  onPhase?: (phase: UploadPhase) => void,
): Promise<void> {
  onPhase?.("preparing");
  const { full, thumbnail } = await compressImagePair(file);

  // The upload policy pins the content type, and storage ignores the file
  // part's own header — so a blob that failed to re-encode would be stored
  // under a type its bytes do not match, giving an image nothing can render.
  if (full.type && full.type !== "image/jpeg") {
    throw new Error("This image format could not be processed. Try another file.");
  }

  onPhase?.("uploading");
  const presigned = await send<PresignResponse>("/api/fleet/attachments/presign", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      owner,
      ownerId,
      contentType: "image/jpeg",
      withThumbnail: thumbnail !== null,
    }),
  });

  // Together, not one after the other. These are two independent writes to two
  // signed keys — the ordering constraint is between the pair and the confirm
  // that follows, which the await below still enforces. The caller uploads one
  // file at a time, so this adds one connection, not a flood.
  await Promise.all([
    postToStorage(presigned.image, full),
    thumbnail && presigned.thumbnail
      ? postToStorage(presigned.thumbnail, thumbnail)
      : Promise.resolve(),
  ]);

  await send<string>("/api/fleet/attachments/confirm", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      owner,
      ownerId,
      uploadId: presigned.uploadId,
      withThumbnail: thumbnail !== null,
      originalFileName: file.name,
      caption: caption ?? null,
    }),
  });
}

async function postToStorage(target: PresignedTarget, blob: Blob): Promise<void> {
  const form = new FormData();
  // Order matters — every signed field first, the file last.
  for (const [k, v] of Object.entries(target.fields)) form.append(k, v);
  form.append("file", blob);

  // No Content-Type header on the request: the browser has to set the
  // multipart boundary itself.
  // target.url is same-origin — the API signs for MinIO's internal host and the
  // response is rewritten to a path this app serves, so a relative URL resolves
  // against whatever address the browser used to load the page.
  const res = await fetch(target.url, { method: "POST", body: form });
  if (!res.ok) {
    // Storage checks the signed size and type conditions before writing
    // anything, so these are expected outcomes rather than faults. A 401 comes
    // from our own relay rather than storage, which is why it reads differently.
    throw new Error(
      res.status === 400
        ? "That image is too large to upload."
        : res.status === 401
          ? "Your session ended. Sign in again and retry."
          : res.status === 403
            ? "The upload link expired. Try again."
            : `Upload failed (${res.status}).`,
    );
  }
}
