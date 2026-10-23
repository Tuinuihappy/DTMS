// One stack for everything that floats over the page — modal dialogs and
// drawers, and non-modal layers such as combobox lists and menus — so the
// keyboard always talks to the topmost one.
//
// Before this, each overlay added its own window keydown listener: one Esc
// closed every open layer at once (order drawer → trip drawer → PASS
// dialog all went together), and nested lists had to remember to
// stopPropagation. Here there is a single listener and a single answer to
// "which layer is on top".
//
// Stacking rule: a layer is always above its parent (the layer whose
// React subtree it renders in — see LayerContext in use-layer.ts), and
// among unrelated layers the one opened later is above. React runs child
// effects before parent effects, so a child that opens in the same commit
// as its parent registers FIRST; the parent rule keeps it on top anyway.
//
// z-index band: 100 + position × 10. Above all page chrome (≤ z-50) and
// below toasts (z-[200], primitives/toast.tsx) so a toast is never
// hidden behind a dialog. Nine layers fit before the band is clamped.
//
// Kept free of React so it can be tested with `node --test` (pass a bare
// EventTarget instead of window).

export const LAYER_Z_BASE = 100;
export const LAYER_Z_STEP = 10;
export const LAYER_Z_MAX = 190;

type Ref<T> = { current: T };

export type LayerRegistration = {
  id: string;
  parentId: string | null;
  modal: boolean;
  dismissible: Ref<boolean>;
  onDismiss: Ref<() => void>;
};

type Layer = LayerRegistration & { order: number };

type FocusTarget = {
  focus(options?: { preventScroll?: boolean }): void;
};

// Minimal shape of the panel node the Tab trap needs — an HTMLElement in
// the browser, a stub in tests.
export type LayerNode = FocusTarget & {
  contains(other: unknown): boolean;
  querySelectorAll(selectors: string): ArrayLike<unknown>;
};

const FOCUSABLE = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled]):not([type=hidden])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  "[tabindex]:not([tabindex='-1'])",
  "[contenteditable=true]",
].join(",");

/** Marks the focus guards ModalLayer renders before and after a panel. */
export const FOCUS_GUARD_ATTR = "data-focus-guard";

function isFocusGuard(el: unknown): boolean {
  const g = el as { hasAttribute?: (name: string) => boolean } | null;
  return typeof g?.hasAttribute === "function" && g.hasAttribute(FOCUS_GUARD_ATTR);
}

// Inputs whose own picker uses Esc — let the browser close the picker
// first instead of closing the dialog under it.
const PICKER_INPUT_TYPES = new Set(["date", "time", "datetime-local", "month", "week"]);

function isVisible(el: unknown): boolean {
  const r = (el as { getClientRects?: () => ArrayLike<unknown> }).getClientRects;
  return typeof r === "function" ? r.call(el).length > 0 : true;
}

export function focusablesIn(node: LayerNode): FocusTarget[] {
  return Array.from(node.querySelectorAll(FOCUSABLE)).filter(isVisible) as FocusTarget[];
}

// The selector can't know everything the browser skips (it listed a
// disabled-looking motion.button the browser wouldn't Tab to), so "first"
// and "last" are decided by actually focusing: try candidates in order and
// keep the first one that really took focus.
function tryFocusIn(node: LayerNode, fromEnd: boolean): boolean {
  const items = focusablesIn(node);
  if (fromEnd) items.reverse();
  const doc = (globalThis as { document?: { activeElement: unknown } }).document;
  for (const el of items) {
    el.focus({ preventScroll: true });
    if (!doc || doc.activeElement === el) return true;
  }
  node.focus({ preventScroll: true });
  return false;
}

export function focusFirstIn(node: LayerNode): boolean {
  return tryFocusIn(node, false);
}

export function focusLastIn(node: LayerNode): boolean {
  return tryFocusIn(node, true);
}

export type LayerStack = ReturnType<typeof createLayerStack>;

export function createLayerStack(target: EventTarget | null) {
  const layers = new Map<string, Layer>();
  const nodes = new Map<string, LayerNode>();
  const listeners = new Set<() => void>();
  let orderCounter = 0;
  let listening = false;
  let sorted: Layer[] = [];

  function key(layer: Layer, seen = new Set<string>()): number {
    // max(own order, parent's key) — a child is never below its parent.
    if (seen.has(layer.id)) return layer.order;
    seen.add(layer.id);
    const parent = layer.parentId ? layers.get(layer.parentId) : undefined;
    return parent ? Math.max(layer.order, key(parent, seen) + 0.5) : layer.order;
  }

  function depth(layer: Layer): number {
    let d = 0;
    let p = layer.parentId ? layers.get(layer.parentId) : undefined;
    while (p && d < 50) {
      d++;
      p = p.parentId ? layers.get(p.parentId) : undefined;
    }
    return d;
  }

  function resort() {
    sorted = Array.from(layers.values()).sort(
      (a, b) => key(a) - key(b) || depth(a) - depth(b),
    );
    for (const l of listeners) l();
  }

  function top(): Layer | undefined {
    return sorted[sorted.length - 1];
  }

  function topModal(): Layer | undefined {
    for (let i = sorted.length - 1; i >= 0; i--) if (sorted[i].modal) return sorted[i];
    return undefined;
  }

  function onKeyDown(event: Event) {
    const e = event as KeyboardEvent;
    if (e.defaultPrevented || e.isComposing) return;

    if (e.key === "Escape") {
      // Holding Esc would otherwise peel off one layer per key repeat.
      if (e.repeat) return;
      const t = e.target as { tagName?: string; type?: string } | null;
      if (t?.tagName === "INPUT" && PICKER_INPUT_TYPES.has(t.type ?? "")) return;
      const layer = top();
      if (!layer) return;
      e.preventDefault();
      // Not dismissible (busy) → swallow, so it can't fall through and
      // close the layer underneath either.
      if (layer.dismissible.current) layer.onDismiss.current();
      return;
    }

    if (e.key === "Tab") {
      // Wrapping at the panel's edges is done by focus guards around the
      // panel (ModalLayer): the browser itself decides what is tabbable and
      // lands on a guard, which sends focus back in. Here we only pull focus
      // in when it is somewhere else entirely (the page behind, <body>).
      const layer = topModal();
      const node = layer ? nodes.get(layer.id) : undefined;
      if (!node) return;
      const active = (globalThis as { document?: { activeElement: unknown } }).document
        ?.activeElement;
      if (!node.contains(active) && !isFocusGuard(active)) {
        e.preventDefault();
        if (e.shiftKey) focusLastIn(node);
        else focusFirstIn(node);
      }
    }
  }

  function syncListener() {
    if (!target) return;
    if (layers.size > 0 && !listening) {
      target.addEventListener("keydown", onKeyDown);
      listening = true;
    } else if (layers.size === 0 && listening) {
      target.removeEventListener("keydown", onKeyDown);
      listening = false;
    }
  }

  return {
    /** Join the stack. Returns the function that leaves it. */
    register(reg: LayerRegistration): () => void {
      const layer: Layer = { ...reg, order: ++orderCounter };
      layers.set(reg.id, layer);
      syncListener();
      resort();
      return () => {
        if (layers.get(reg.id) !== layer) return;
        layers.delete(reg.id);
        syncListener();
        resort();
      };
    },

    /** The panel node the Tab trap keeps focus in (modal layers). */
    setNode(id: string, node: LayerNode | null) {
      if (node) nodes.set(id, node);
      else nodes.delete(id);
    },

    nodeOf(id: string): LayerNode | undefined {
      return nodes.get(id);
    },

    /** z-index for the layer, or null when it isn't open. */
    zOf(id: string): number | null {
      const i = sorted.findIndex((l) => l.id === id);
      if (i < 0) return null;
      return Math.min(LAYER_Z_BASE + i * LAYER_Z_STEP, LAYER_Z_MAX);
    },

    topId(): string | null {
      return top()?.id ?? null;
    },

    topModalId(): string | null {
      return topModal()?.id ?? null;
    },

    isOpen(id: string): boolean {
      return layers.has(id);
    },

    size(): number {
      return layers.size;
    },

    isListening(): boolean {
      return listening;
    },

    subscribe(listener: () => void): () => void {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },

    /** Exposed for tests. */
    handleKeyDown: onKeyDown,
  };
}

/** The app-wide stack, listening on window (bubble phase — see use-layer.ts). */
export const layerStack = createLayerStack(typeof window === "undefined" ? null : window);
