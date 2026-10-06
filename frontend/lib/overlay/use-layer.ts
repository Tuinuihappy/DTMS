"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  useSyncExternalStore,
  type RefObject,
} from "react";
import { focusablesIn, layerStack } from "./layer-stack";

// React side of layer-stack.ts. Every overlay — modal or not — joins the
// stack through useLayer, so Esc and Tab always go to the topmost one.
//
// The shared keydown listener sits on window, bubble phase. Next hydrates
// into `document`, so React handlers run first; anything that calls
// preventDefault (or stopPropagation) on a key keeps it from the stack.

/** Id of the layer whose subtree we're rendering in — children sit above it. */
export const LayerContext = createContext<string | null>(null);

export function useLayer({
  open,
  onDismiss,
  dismissible = true,
  modal,
}: {
  open: boolean;
  onDismiss: () => void;
  dismissible?: boolean;
  modal: boolean;
}): { id: string; z: number | null } {
  // From useId at render, not from the effect, so a child that renders in
  // this subtree can name us as its parent before either effect has run.
  const id = useId();
  const parentId = useContext(LayerContext);
  const onDismissRef = useRef(onDismiss);
  const dismissibleRef = useRef(dismissible);
  useLayoutEffect(() => {
    onDismissRef.current = onDismiss;
    dismissibleRef.current = dismissible;
  });

  useEffect(() => {
    // Joins when open turns true and leaves when it turns false — NOT on
    // unmount of the panel: AnimatePresence keeps an exiting panel mounted
    // with frozen props, and it must not keep claiming the keyboard.
    if (!open) return;
    return layerStack.register({
      id,
      parentId,
      modal,
      dismissible: dismissibleRef,
      onDismiss: onDismissRef,
    });
  }, [open, id, parentId, modal]);

  const liveZ = useSyncExternalStore(
    layerStack.subscribe,
    () => layerStack.zOf(id),
    () => null,
  );
  // Keep the last z while an exiting panel animates out, so it doesn't
  // drop under the page for its final frames.
  const lastZ = useRef<number | null>(null);
  if (liveZ !== null) lastZ.current = liveZ;
  return { id, z: liveZ ?? lastZ.current };
}

/** Non-modal layers (combobox lists, menus, popovers): Esc only, no Tab trap. */
export function useDismissableLayer(opts: {
  open: boolean;
  onDismiss: () => void;
}): { id: string; z: number | null } {
  return useLayer({ ...opts, modal: false });
}

export type DialogInitialFocus = "auto" | "panel" | RefObject<HTMLElement | null>;

export type DialogPanelProps = {
  ref: (node: HTMLElement | null) => () => void;
  role: "dialog" | "alertdialog";
  "aria-modal": true;
  "aria-labelledby"?: string;
  "aria-label"?: string;
  tabIndex: -1;
};

const FIELD = "input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled])";

// Touch-first devices (tablets, the operator PWA): focusing a field on
// open pops the on-screen keyboard over the dialog. Hover-capable devices
// with a touchscreen still count as desktop.
function isTouchFirst(): boolean {
  return typeof window !== "undefined" &&
    window.matchMedia?.("(hover: none) and (pointer: coarse)").matches === true;
}

function resolveInitialFocus(node: HTMLElement, initial: DialogInitialFocus): HTMLElement {
  if (typeof initial === "object") return initial.current ?? node;
  if (initial === "panel" || isTouchFirst()) return node;
  // "auto" looks at form fields only: nearly every panel has a header X
  // button before its fields, and focus landing on X helps nobody.
  const field = Array.from(node.querySelectorAll<HTMLElement>(FIELD)).find(
    (el) => el.getClientRects().length > 0,
  );
  return field ?? node;
}

/**
 * Modal layer: Esc via the stack, Tab trapped in the panel, focus moved in
 * on open and handed back on close, and the dialog ARIA attributes.
 *
 * Call it in a component that stays mounted while the panel animates out
 * (outside AnimatePresence) and spread `panelProps` on the panel element.
 * ModalLayer (components/primitives/modal-layer.tsx) does all of this.
 */
export function useDialog({
  open,
  onClose,
  dismissible = true,
  initialFocus = "auto",
  label,
  role = "dialog",
}: {
  open: boolean;
  onClose: () => void;
  dismissible?: boolean;
  initialFocus?: DialogInitialFocus;
  /** aria-label when the panel has no visible heading for titleId. */
  label?: string;
  role?: "dialog" | "alertdialog";
}): { id: string; z: number | null; titleId: string; panelProps: DialogPanelProps } {
  const { id, z } = useLayer({ open, onDismiss: onClose, dismissible, modal: true });
  const titleId = `${id}-title`;
  const [node, setNode] = useState<HTMLElement | null>(null);
  const initialFocusRef = useRef(initialFocus);
  useLayoutEffect(() => {
    initialFocusRef.current = initialFocus;
  });

  const ref = useCallback(
    (el: HTMLElement | null) => {
      setNode(el);
      if (el) layerStack.setNode(id, el);
      return () => {
        // Only clear if still ours — an exiting panel unmounting after a
        // re-open must not wipe the new panel's node.
        setNode((cur) => (cur === el ? null : cur));
        if (layerStack.nodeOf(id) === el) layerStack.setNode(id, null);
      };
    },
    [id],
  );

  // Where focus goes back to on close: whatever had it when we opened.
  const returnFocus = useRef<HTMLElement | null>(null);
  const nodeRef = useRef<HTMLElement | null>(null);
  nodeRef.current = node;
  useEffect(() => {
    if (!open) return;
    const active = document.activeElement;
    returnFocus.current =
      active instanceof HTMLElement && active !== document.body ? active : null;
    return () => {
      // Hand focus back only if it is still ours (inside the panel) or
      // lost (body / detached). If the user or another layer already put
      // it somewhere real, leave it. Works whichever of two layers closing
      // together cleans up first.
      const now = document.activeElement;
      const panel = nodeRef.current;
      const ours =
        !now || now === document.body || !now.isConnected || (panel?.contains(now) ?? false);
      if (!ours) return;
      const back = returnFocus.current;
      if (back?.isConnected) {
        back.focus({ preventScroll: true });
        return;
      }
      const topModal = layerStack.topModalId();
      const fallback = topModal ? layerStack.nodeOf(topModal) : undefined;
      (fallback as HTMLElement | undefined)?.focus({ preventScroll: true });
    };
  }, [open]);

  // Move focus in once per open, after the panel node exists (it mounts
  // inside AnimatePresence) and only if we are the topmost modal — when a
  // drawer and its dialog open together the dialog's effect runs first.
  const focused = useRef(false);
  useEffect(() => {
    if (!open) {
      focused.current = false;
      return;
    }
    if (!node || focused.current) return;
    focused.current = true;
    if (layerStack.topModalId() !== id) return;
    if (node.contains(document.activeElement)) return; // e.g. a field's autoFocus
    resolveInitialFocus(node, initialFocusRef.current).focus({ preventScroll: true });
  }, [open, node, id]);

  return {
    id,
    z,
    titleId,
    panelProps: {
      ref,
      role,
      "aria-modal": true,
      ...(label ? { "aria-label": label } : { "aria-labelledby": titleId }),
      tabIndex: -1,
    },
  };
}

export { focusablesIn };
