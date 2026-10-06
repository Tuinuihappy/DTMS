"use client";

import { AnimatePresence } from "motion/react";
import type { CSSProperties, KeyboardEvent, ReactNode, RefObject } from "react";
import { createPortal } from "react-dom";
import { OverlayBackdrop } from "@/components/primitives/overlay-backdrop";
import { usePortalTarget } from "@/components/primitives/overlay-root";
import {
  LayerContext,
  useDialog,
  type DialogInitialFocus,
  type DialogPanelProps,
} from "@/lib/overlay/use-layer";

// The frame every modal overlay (dialog, drawer, lightbox) renders in:
//   - portalled out of its host, so a transformed / backdrop-filtered /
//     overflow-hidden ancestor can't clip or re-anchor `position: fixed`
//     (the photo lightbox bug, 7cba1082),
//   - an OverlayBackdrop that stays mounted (see its comment),
//   - AnimatePresence for the panel,
//   - z-index from the layer stack, keyboard via useDialog (Esc closes the
//     topmost layer only, Tab trapped, focus in on open and back on close),
//   - LayerContext, so overlays opened from inside sit above this one.
//
// The panel markup stays the caller's: render it from `children`, spread
// `panelProps` on the element that holds the content, put `id={titleId}`
// on the heading, and `style={panelStyle}` on the positioned element.
//
//   <ModalLayer open={open} onClose={onClose} dismissible={!busy}
//               backdropClassName="bg-[var(--color-ink-900)]/55 backdrop-blur-md">
//     {({ panelProps, titleId, panelStyle }) => (
//       <motion.div key="x" {...panelProps} style={panelStyle} className="fixed … outline-none">
//         <h2 id={titleId}>…</h2>
//       </motion.div>
//     )}
//   </ModalLayer>
//
// intent="irreversible" is for dialogs whose confirm can't be undone or
// moves something physical (robot PASS, cancel trip, delete). It needs the
// safe button's ref and then: role="alertdialog", focus starts on the safe
// button, and Enter in a text input won't submit the form — the confirm
// button has to be pressed.

export type ModalLayerRender = {
  panelProps: DialogPanelProps & { onKeyDown?: (e: KeyboardEvent<HTMLElement>) => void };
  titleId: string;
  /** zIndex for the panel's positioned element (one above the backdrop). */
  panelStyle: CSSProperties | undefined;
};

type BaseProps = {
  open: boolean;
  onClose: () => void;
  /** false while busy/saving: Esc and backdrop clicks are ignored. */
  dismissible?: boolean;
  /** Close on backdrop click (default true, still subject to dismissible). */
  closeOnBackdrop?: boolean;
  /** aria-label, for panels without a visible heading to put titleId on. */
  label?: string;
  /** Tint / blur classes for the backdrop. No z- class needed. */
  backdropClassName?: string;
  children: (render: ModalLayerRender) => ReactNode;
};

type ModalLayerProps = BaseProps &
  (
    | { intent?: "default"; initialFocus?: DialogInitialFocus; safeRef?: never }
    | { intent: "irreversible"; safeRef: RefObject<HTMLElement | null>; initialFocus?: never }
  );

const NON_TEXT_INPUTS = new Set(["button", "submit", "reset", "checkbox", "radio", "file"]);

function blockImplicitSubmit(e: KeyboardEvent<HTMLElement>) {
  const t = e.target;
  if (e.key === "Enter" && t instanceof HTMLInputElement && !NON_TEXT_INPUTS.has(t.type)) {
    e.preventDefault();
  }
}

export function ModalLayer(props: ModalLayerProps) {
  const {
    open,
    onClose,
    dismissible = true,
    closeOnBackdrop = true,
    label,
    backdropClassName,
    children,
  } = props;
  const irreversible = props.intent === "irreversible";
  const { id, z, titleId, panelProps } = useDialog({
    open,
    onClose,
    dismissible,
    label,
    role: irreversible ? "alertdialog" : "dialog",
    initialFocus: irreversible ? props.safeRef : props.initialFocus,
  });
  const target = usePortalTarget();
  if (!target) return null;

  const render: ModalLayerRender = {
    panelProps: irreversible ? { ...panelProps, onKeyDown: blockImplicitSubmit } : panelProps,
    titleId,
    panelStyle: z != null ? { zIndex: z + 1 } : undefined,
  };

  return createPortal(
    <LayerContext.Provider value={id}>
      <OverlayBackdrop
        open={open}
        onClick={closeOnBackdrop && dismissible ? onClose : undefined}
        className={backdropClassName}
        zIndex={z}
      />
      <AnimatePresence>{open ? children(render) : null}</AnimatePresence>
    </LayerContext.Provider>,
    target,
  );
}
