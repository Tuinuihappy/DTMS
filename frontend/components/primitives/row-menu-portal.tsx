"use client";

import { AnimatePresence, motion } from "motion/react";
import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { createPortal } from "react-dom";

// Row-level dropdown menus get clipped by `overflow-hidden` on
// DataTableShell + `overflow-x-auto` on its inner scroll container —
// the menu has to escape both. We render it into a portal at the body
// root with `position: fixed`, anchored to the trigger's bounding rect.
// The menu re-aligns on scroll/resize while open so it stays glued to
// the trigger as the page moves around it.

type Anchor = {
  top: number;
  right: number;
  bottom: number;
};

export function RowMenuPortal({
  open,
  onClose,
  triggerRef,
  width = 192,
  ariaLabel,
  children,
}: {
  open: boolean;
  onClose: () => void;
  triggerRef: React.RefObject<HTMLElement | null>;
  // Approximate menu width in px — used to right-align the menu under
  // the trigger so it matches the original `right-0` look. Pass the
  // same value as the visual `w-*` class on the menu container.
  width?: number;
  ariaLabel?: string;
  children: ReactNode;
}) {
  const [anchor, setAnchor] = useState<Anchor | null>(null);
  const [mounted, setMounted] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  // SSR safety — `document` only exists in the browser, so we defer
  // mounting the portal until after hydration.
  useEffect(() => {
    setMounted(true);
  }, []);

  const recompute = () => {
    const el = triggerRef.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    setAnchor({ top: r.top, right: r.right, bottom: r.bottom });
  };

  // Compute the anchor synchronously after the trigger paints — using
  // useLayoutEffect avoids the one-frame flicker where the menu would
  // otherwise mount at (0,0) before re-aligning.
  useLayoutEffect(() => {
    if (!open) return;
    recompute();
    // Re-align on scroll/resize so the menu follows the trigger if the
    // page shifts while open. `true` for capture so we catch scrolls in
    // ancestor scroll containers too (the table's inner overflow-x-auto).
    const handle = () => recompute();
    window.addEventListener("scroll", handle, true);
    window.addEventListener("resize", handle);
    return () => {
      window.removeEventListener("scroll", handle, true);
      window.removeEventListener("resize", handle);
    };
    // recompute is intentionally not memoised — it reads the ref each
    // call. open is the only dep that matters for re-running setup.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Close on outside click + Escape. The trigger button has its own
  // toggle handler, so we ignore clicks that hit the trigger itself —
  // otherwise opening would immediately close.
  useEffect(() => {
    if (!open) return;
    const onClick = (e: MouseEvent) => {
      const target = e.target as Node;
      if (menuRef.current?.contains(target)) return;
      if (triggerRef.current?.contains(target)) return;
      onClose();
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    const timer = setTimeout(() => {
      window.addEventListener("click", onClick);
      window.addEventListener("keydown", onKey);
    }, 0);
    return () => {
      clearTimeout(timer);
      window.removeEventListener("click", onClick);
      window.removeEventListener("keydown", onKey);
    };
  }, [open, onClose, triggerRef]);

  if (!mounted) return null;

  return createPortal(
    <AnimatePresence>
      {open && anchor && (
        <motion.div
          key="row-menu"
          ref={menuRef}
          initial={{ opacity: 0, scale: 0.94, y: -4 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.96, y: -4, transition: { duration: 0.12 } }}
          transition={{ type: "spring", stiffness: 460, damping: 30 }}
          role="menu"
          aria-label={ariaLabel}
          style={{
            position: "fixed",
            top: anchor.bottom + 4,
            left: anchor.right - width,
            width,
            zIndex: 60,
          }}
          className="origin-top-right overflow-hidden rounded-xl glass-strong shadow-[0_20px_50px_-15px_rgba(15,23,42,0.35)]"
        >
          {children}
        </motion.div>
      )}
    </AnimatePresence>,
    document.body,
  );
}
