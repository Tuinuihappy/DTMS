"use client";

import {
  useEffect,
  useLayoutEffect,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";
import { useDismissableLayer } from "@/lib/overlay/use-layer";

// Combobox dropdown lists rendered in-tree lose to sibling stacking
// contexts: every mission-row / section card carries backdrop-filter (and
// motion transforms), each of which isolates its own stacking context, so
// an absolute z-30 list inside card N paints UNDER card N+1 regardless of
// z-index. Same class of bug RowMenuPortal solved for row menus — the fix
// is the same: render at document.body with position:fixed, anchored to
// the trigger's viewport rect, so no ancestor stacking context or
// overflow clip can ever affect the list. Re-anchors on scroll/resize
// (capture) so it stays glued to the input while the page moves.

type Rect = { top: number; left: number; width: number };

// The open list is a non-modal layer: Esc closes the list (not the dialog
// it sits in — the list is that dialog's child layer) and its z-index
// comes from the stack, so it is always above the dialog that hosts it.
export function ComboboxListPortal({
  open,
  anchorRef,
  onDismiss,
  children,
}: {
  open: boolean;
  // The combobox wrapper — the list matches its width and opens under it.
  anchorRef: React.RefObject<HTMLElement | null>;
  /** Close the list (Esc). */
  onDismiss: () => void;
  children: ReactNode;
}) {
  const { z } = useDismissableLayer({ open, onDismiss });
  const [rect, setRect] = useState<Rect | null>(null);
  const [mounted, setMounted] = useState(false);

  // SSR safety — document only exists in the browser.
  useEffect(() => {
    setMounted(true);
  }, []);

  useLayoutEffect(() => {
    if (!open) return;
    const recompute = () => {
      const el = anchorRef.current;
      if (!el) return;
      const r = el.getBoundingClientRect();
      setRect({ top: r.bottom + 4, left: r.left, width: r.width });
    };
    recompute();
    window.addEventListener("scroll", recompute, true);
    window.addEventListener("resize", recompute);
    return () => {
      window.removeEventListener("scroll", recompute, true);
      window.removeEventListener("resize", recompute);
    };
  }, [open, anchorRef]);

  if (!mounted || !open || !rect) return null;

  return createPortal(
    <div
      style={{
        position: "fixed",
        top: rect.top,
        left: rect.left,
        width: rect.width,
        zIndex: z ?? 60,
      }}
    >
      {children}
    </div>,
    document.body,
  );
}
