"use client";

import { useEffect } from "react";

// Phase 4.5 — Mounts the operator PWA service worker.
//
// Why a dedicated client component (vs putting this in providers.tsx):
//   - Admin routes never load the SW. Registration only fires when the
//     operator shell layout mounts.
//   - SW scope is /m/ — registering it from anywhere on the site would
//     waste a worker registration on admin sessions that won't use it.
//
// The SW file itself lives at /public/sw.js (Phase 4.3). This component
// re-registers on every operator page load — safe because the browser
// no-ops when the bytes haven't changed; new bytes trigger an SW.update()
// + the activate event that claims clients immediately (see sw.js).
export function ServiceWorkerRegistrar() {
  useEffect(() => {
    if (typeof window === "undefined") return;
    if (!("serviceWorker" in navigator)) {
      console.warn("[operator-pwa] Service Workers not supported — push + offline disabled.");
      return;
    }
    const register = async () => {
      try {
        const reg = await navigator.serviceWorker.register("/sw.js", { scope: "/m/" });
        // Trigger update check on every shell mount — covers PWAs left
        // open across releases that wouldn't otherwise pick up the new SW.
        await reg.update();
      } catch (err) {
        console.error("[operator-pwa] SW registration failed:", err);
      }
    };
    register();
  }, []);
  return null;
}
