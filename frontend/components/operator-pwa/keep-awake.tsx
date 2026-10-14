"use client";

import { usePathname } from "next/navigation";
import { useEffect } from "react";
import { isWakeLockSupported, keepAwakePref, PREFS_CHANGED_EVENT } from "@/lib/operator-pwa/alert-output";

// Keeps the tablet screen on while the operator app is in front, so the
// in-app new-trip / trip-ended alerts can actually be heard — they only
// run while the page is visible. Per-device switch in Settings, default on.
//
// Chrome releases a wake lock whenever the page is hidden (screen off by
// the power button, another app), so it is re-requested each time the page
// comes back. The power button still turns the screen off.
//
// Trade-off accepted 2026-10-14: with the lock held the device doesn't
// auto-lock, so a logged-in tablet left unattended stays usable.
export function KeepAwake() {
  const pathname = usePathname();
  const enabled = !pathname?.startsWith("/m/login");

  useEffect(() => {
    if (!enabled || !isWakeLockSupported()) return;
    let sentinel: WakeLockSentinel | null = null;
    let cancelled = false;

    const release = () => {
      const s = sentinel;
      sentinel = null;
      void s?.release().catch(() => {});
    };

    const sync = async () => {
      if (cancelled) return;
      const want = keepAwakePref.get() && document.visibilityState === "visible";
      if (!want) {
        release();
        return;
      }
      if (sentinel && !sentinel.released) return;
      try {
        const s = await navigator.wakeLock.request("screen");
        if (cancelled || !keepAwakePref.get()) {
          void s.release().catch(() => {});
          return;
        }
        sentinel = s;
      } catch {
        // Battery saver, or the page went hidden mid-request — the next
        // visibility change tries again.
      }
    };

    const onStorage = (e: StorageEvent) => {
      if (e.key === null || e.key.startsWith("operator:")) void sync();
    };

    void sync();
    document.addEventListener("visibilitychange", sync);
    window.addEventListener(PREFS_CHANGED_EVENT, sync);
    window.addEventListener("storage", onStorage);
    return () => {
      cancelled = true;
      document.removeEventListener("visibilitychange", sync);
      window.removeEventListener(PREFS_CHANGED_EVENT, sync);
      window.removeEventListener("storage", onStorage);
      release();
    };
  }, [enabled]);

  return null;
}
