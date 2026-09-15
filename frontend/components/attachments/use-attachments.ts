"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import {
  deleteAttachment,
  listAttachments,
  uploadAttachment,
  type Attachment,
  type AttachmentOwner,
  type UploadPhase,
} from "@/lib/api/fleet-attachments";

/**
 * One owner's images: the list, and adding and removing them. Shared by the
 * gallery and by the single-photo view a table opens, so both keep a caller's
 * row in step the same way.
 *
 * Pass a null owner to load nothing — for a view that stays mounted so it can
 * animate out after its target is gone.
 */
export function useAttachments(
  owner: AttachmentOwner | null,
  ownerId: string | null,
  /** The authoritative list after it loads, after an upload, and after a
   *  delete — only ever on success, so a failed load can never tell the caller
   *  there are no images. Lets a table show the right cover without refetching. */
  onItemsChanged?: (items: Attachment[]) => void,
) {
  const [items, setItems] = useState<Attachment[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [phase, setPhase] = useState<UploadPhase | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Held in a ref so refresh keeps its identity — a new callback from the parent
  // on every render must not re-run the load effect.
  const onItemsChangedRef = useRef(onItemsChanged);
  useEffect(() => {
    onItemsChangedRef.current = onItemsChanged;
  });

  const refresh = useCallback(
    (signal?: AbortSignal) => {
      if (!owner || !ownerId) return Promise.resolve();
      return listAttachments(owner, ownerId, signal)
        .then((list) => {
          setItems(list);
          onItemsChangedRef.current?.(list);
        })
        .catch((e: Error) => {
          if (e.name !== "AbortError") setError(e.message);
        })
        .finally(() => {
          if (!signal?.aborted) setLoading(false);
        });
    },
    [owner, ownerId],
  );

  useEffect(() => {
    if (!owner || !ownerId) return;
    const ac = new AbortController();
    setItems([]);
    setError(null);
    setLoading(true);
    void refresh(ac.signal);
    return () => ac.abort();
  }, [owner, ownerId, refresh]);

  const upload = async (files: File[]) => {
    if (!owner || !ownerId || files.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      // Sequential, not parallel. Each upload is three round trips plus the
      // bytes, and the per-owner ceiling is checked server-side per call —
      // firing them at once would race that check and flood a phone's uplink.
      for (const file of files) {
        await uploadAttachment(owner, ownerId, file, null, setPhase);
      }
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.");
      // Some may have succeeded before the failure; show what actually landed.
      await refresh();
    } finally {
      setBusy(false);
      setPhase(null);
    }
  };

  /** Resolves true once the image is gone. */
  const remove = async (id: string): Promise<boolean> => {
    if (!owner || !ownerId) return false;
    setBusy(true);
    setError(null);
    try {
      await deleteAttachment(owner, ownerId, id);
      const next = items.filter((x) => x.id !== id);
      setItems(next);
      onItemsChangedRef.current?.(next);
      return true;
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not delete the image.");
      return false;
    } finally {
      setBusy(false);
    }
  };

  return { items, loading, busy, phase, error, upload, remove };
}
