"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { drainQueue, getQueueDepth } from "@/lib/operator-pwa/offline-queue";

// Phase 4.5 — Sticky header on /m/* shell pages. Renders:
//   - Page title
//   - Online/offline pill
//   - Queue depth pill (link to settings on tap → drain)
//   - Settings cog
//
// Polls navigator.onLine + queue depth every 4s so the operator gets
// near-real-time feedback that their offline writes are landing once
// the network returns. Background drain is auto-triggered on the
// 'online' window event below.
export function OperatorShellHeader({ title }: { title: string }) {
  const [online, setOnline] = useState(true);
  const [queueDepth, setQueueDepth] = useState(0);

  useEffect(() => {
    const updateOnline = () => setOnline(navigator.onLine);
    updateOnline();

    const refreshDepth = async () => {
      try {
        setQueueDepth(await getQueueDepth());
      } catch {
        // IDB unavailable (private mode?) — ignore.
      }
    };

    const onOnline = async () => {
      updateOnline();
      const result = await drainQueue();
      setQueueDepth(result.remaining);
    };
    const onOffline = () => updateOnline();
    window.addEventListener("online", onOnline);
    window.addEventListener("offline", onOffline);

    refreshDepth();
    const tick = window.setInterval(refreshDepth, 4_000);
    return () => {
      window.removeEventListener("online", onOnline);
      window.removeEventListener("offline", onOffline);
      window.clearInterval(tick);
    };
  }, []);

  return (
    <header className="sticky top-0 z-10 flex items-center justify-between gap-3 border-b border-zinc-800 bg-zinc-950/85 px-4 py-3 backdrop-blur">
      <h1 className="truncate text-base font-semibold">{title}</h1>
      <div className="flex items-center gap-2">
        {!online && (
          <span className="rounded-full bg-amber-500/15 px-2 py-0.5 text-xs text-amber-300">
            Offline
          </span>
        )}
        {queueDepth > 0 && (
          <Link
            href="/m/settings"
            className="rounded-full bg-zinc-100/10 px-2 py-0.5 text-xs text-zinc-200"
          >
            {queueDepth} queued
          </Link>
        )}
        <Link
          href="/m/settings"
          className="rounded-full bg-zinc-900 px-3 py-1.5 text-xs text-zinc-300"
        >
          Settings
        </Link>
      </div>
    </header>
  );
}
