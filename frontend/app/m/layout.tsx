import type { Metadata, Viewport } from "next";
import { ServiceWorkerRegistrar } from "@/components/operator-pwa/service-worker-registrar";

// Phase 4.5 — Operator PWA shell. Scope is /m/* — admin / dispatcher
// routes outside this layout do not load the operator service worker,
// manifest, or push subscription bootstrap.
//
// The shell is intentionally minimal: dark theme, single-column,
// optimized for portrait phone use. Everything else (header, nav,
// content) is per-page so the trip-detail screen can take full
// height without fighting a layout chrome.

export const metadata: Metadata = {
  title: "DTMS Operator",
  description: "Manual transport operator app for DTMS delivery jobs.",
  manifest: "/manifest.webmanifest",
  appleWebApp: {
    capable: true,
    statusBarStyle: "black-translucent",
    title: "DTMS Operator",
  },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  maximumScale: 1,
  themeColor: "#0a0a0a",
};

export default function OperatorLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="min-h-dvh bg-zinc-950 text-zinc-50">
      <ServiceWorkerRegistrar />
      {children}
    </div>
  );
}
