"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

// Where ModalLayer portals to. Default is <body>; a section whose overlays
// must inherit styles from a wrapper (the operator PWA sets its dark zinc
// text colour on a div in app/m/layout.tsx, not on body) wraps its content
// in <OverlayRoot>, and overlays opened inside portal into the div it
// renders at the end of that wrapper instead.

// undefined = no OverlayRoot above (use body); null = inside one whose
// container hasn't mounted yet (render nothing for that first pass).
const OverlayRootContext = createContext<HTMLElement | null | undefined>(undefined);

export function OverlayRoot({ children }: { children: ReactNode }) {
  const [el, setEl] = useState<HTMLElement | null>(null);
  return (
    <OverlayRootContext.Provider value={el}>
      {children}
      <div ref={setEl} data-overlay-root="" />
    </OverlayRootContext.Provider>
  );
}

/**
 * The element overlays should portal into, or null while it isn't
 * available yet (server render, or an OverlayRoot not yet mounted).
 */
export function usePortalTarget(): HTMLElement | null {
  const root = useContext(OverlayRootContext);
  const [mounted, setMounted] = useState(false);
  // Flip after the first client render so SSR and hydration agree (null).
  useEffect(() => setMounted(true), []);
  if (!mounted) return null;
  return root === undefined ? document.body : root;
}
