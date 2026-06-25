"use client";

import { useRef, useState } from "react";
import { presignPod, uploadPodBytes } from "@/lib/api/operator";

// Phase 4.5 — POD capture flow:
//   1. Operator taps "Take photo" → file input opens camera (mobile)
//      or file picker (desktop dev). `capture="environment"` hints
//      back camera on phones.
//   2. Onchange → component asks the backend for a presigned PUT URL
//      via /api/operator/pod/presign.
//   3. PUTs the chosen file's bytes directly to MinIO.
//   4. Calls onCaptured(objectKey) so the parent's action button can
//      submit the pickup/drop with the key attached.
//
// Stays online-only — presigning requires a fresh URL from MinIO, so
// when the device is offline we surface a clear "you must be online"
// message instead of queuing (the URL would be expired by replay time).
type Props = {
  tripId: string;
  kind: "pickup" | "drop";
  podKey: string | null;
  onCaptured: (key: string) => void;
};

export function PodCapture({ tripId, kind, podKey, onCaptured }: Props) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const presigned = await presignPod(tripId, kind);
      await uploadPodBytes(presigned.uploadUrl, file);
      onCaptured(presigned.objectKey);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed.");
    } finally {
      setBusy(false);
      if (inputRef.current) inputRef.current.value = "";
    }
  };

  return (
    <div className="flex flex-col gap-2">
      <input
        ref={inputRef}
        type="file"
        accept="image/*"
        capture="environment"
        onChange={onChange}
        className="hidden"
      />
      <button
        type="button"
        onClick={() => inputRef.current?.click()}
        disabled={busy}
        className="h-12 rounded-xl border border-zinc-800 bg-zinc-900 text-sm font-medium text-zinc-100 disabled:opacity-50"
      >
        {busy ? "Uploading…" : podKey ? "Replace photo" : "Take photo (recommended)"}
      </button>
      {podKey && !busy && (
        <div className="rounded-lg border border-emerald-900/60 bg-emerald-950/30 px-3 py-2 text-xs text-emerald-200">
          Photo uploaded — ready to confirm.
        </div>
      )}
      {error && (
        <div className="rounded-lg border border-red-900/60 bg-red-950/40 px-3 py-2 text-xs text-red-200">
          {error}
        </div>
      )}
    </div>
  );
}
