import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type ChangeEvent,
  type KeyboardEvent,
} from "react";

/**
 * Makes a text input behave for a USB "keyboard wedge" barcode scanner.
 *
 * A wedge scanner is a keyboard. It decodes the barcode in hardware and then
 * types the result, one key at a time. Two consequences bite on this site's
 * machines, and this hook exists for both:
 *
 * <b>1. It sends key positions, not characters.</b> The scanner reports "the key
 * where S lives" and Windows turns that into a character using whichever layout
 * is active. With the Thai layout on, `SHELF-001` arrives as Thai. So instead of
 * letting the browser insert what the layout produced, this reads
 * `KeyboardEvent.code` — the physical key, identical under every layout — and
 * inserts the character a US layout would have. Shift is honoured, so
 * mixed-case codes survive; Caps Lock is deliberately ignored, because scanners
 * send Shift explicitly and a stray Caps Lock would otherwise invert every letter.
 *
 * <b>2. Many handhelds do not send Enter.</b> Instead of making someone reach
 * for the keyboard after every scan, a burst is recognised by its speed and
 * treated as finished once it stops. A scanner types a character every few
 * milliseconds; a person needs 80ms or more. Only a burst fast enough to be a
 * scanner ends itself — a person typing still presses Enter, so a half-typed
 * `SHELF-00` is never looked up on their behalf.
 *
 * Enter (when the scanner does send it) submits immediately, same as ever.
 *
 * The hook owns the value rather than reading it from props: a scan delivers
 * keystrokes faster than React re-renders, and a handler reading a value from
 * its render closure would build each character onto a stale string and drop
 * most of the code.
 */

/** A burst whose characters arrive this far apart on average, or closer, is a
 *  scanner. Wired handhelds sit around 5-20ms; slower wireless models can reach
 *  ~40ms. A fast human is well above this over three or more keys. If a
 *  particular handheld never auto-submits, it is slower than this — raise it. */
const SCANNER_MAX_AVG_GAP_MS = 50;

/** How long the burst must go quiet before it counts as finished. Long enough
 *  to outlast any pause inside one scan, short enough to feel instant. */
const SCAN_END_IDLE_MS = 120;

/** Two keys pressed in quick succession are keyboard rollover, not a scan. */
const MIN_SCAN_LENGTH = 3;

/** A gap this long starts a new burst, so a person who typed a character and
 *  then scanned does not have their keystroke averaged into the scan. */
const BURST_BREAK_MS = 250;

/** Physical key → [unshifted, shifted] on a US layout. Letters are handled
 *  separately in charForKey. */
const US_LAYOUT: Readonly<Record<string, readonly [string, string]>> = {
  Backquote: ["`", "~"],
  Digit1: ["1", "!"],
  Digit2: ["2", "@"],
  Digit3: ["3", "#"],
  Digit4: ["4", "$"],
  Digit5: ["5", "%"],
  Digit6: ["6", "^"],
  Digit7: ["7", "&"],
  Digit8: ["8", "*"],
  Digit9: ["9", "("],
  Digit0: ["0", ")"],
  Minus: ["-", "_"],
  Equal: ["=", "+"],
  BracketLeft: ["[", "{"],
  BracketRight: ["]", "}"],
  Backslash: ["\\", "|"],
  Semicolon: [";", ":"],
  Quote: ["'", '"'],
  Comma: [",", "<"],
  Period: [".", ">"],
  Slash: ["/", "?"],
  Space: [" ", " "],
  Numpad0: ["0", "0"],
  Numpad1: ["1", "1"],
  Numpad2: ["2", "2"],
  Numpad3: ["3", "3"],
  Numpad4: ["4", "4"],
  Numpad5: ["5", "5"],
  Numpad6: ["6", "6"],
  Numpad7: ["7", "7"],
  Numpad8: ["8", "8"],
  Numpad9: ["9", "9"],
  NumpadDecimal: [".", "."],
  NumpadAdd: ["+", "+"],
  NumpadSubtract: ["-", "-"],
  NumpadMultiply: ["*", "*"],
  NumpadDivide: ["/", "/"],
};

/** The character a US layout produces for a physical key, or null for keys that
 *  do not type one (arrows, Backspace, function keys…). */
export function charForKey(code: string, shift: boolean): string | null {
  if (code.length === 4 && code.startsWith("Key")) {
    const upper = code[3];
    return shift ? upper : upper.toLowerCase();
  }
  const pair = US_LAYOUT[code];
  return pair ? pair[shift ? 1 : 0] : null;
}

export function useWedgeScanner({
  enabled,
  onScan,
}: {
  /** When false the input behaves exactly like a normal text box — use this to
   *  leave free-text modes (names, notes) typable in Thai. */
  enabled: boolean;
  /** A scan finished: either Enter arrived, or a scanner-speed burst went quiet. */
  onScan: (value: string) => void;
}) {
  const [value, setValueState] = useState("");
  const valueRef = useRef("");
  const onScanRef = useRef(onScan);
  const burst = useRef({ first: 0, last: 0, count: 0 });
  const idleTimer = useRef<number | null>(null);
  const pendingCaret = useRef<{ el: HTMLInputElement; pos: number } | null>(null);

  useLayoutEffect(() => {
    onScanRef.current = onScan;
  });

  const setValue = useCallback((next: string) => {
    valueRef.current = next;
    setValueState(next);
  }, []);

  const clearIdle = useCallback(() => {
    if (idleTimer.current !== null) {
      window.clearTimeout(idleTimer.current);
      idleTimer.current = null;
    }
  }, []);

  const resetBurst = useCallback(() => {
    burst.current = { first: 0, last: 0, count: 0 };
  }, []);

  // A controlled input puts the caret at the end whenever its value changes
  // programmatically. Put it back where the character went, so someone fixing
  // a typo in the middle of a code is not thrown to the end on every key.
  useLayoutEffect(() => {
    const pending = pendingCaret.current;
    if (!pending) return;
    pendingCaret.current = null;
    pending.el.setSelectionRange(pending.pos, pending.pos);
  }, [value]);

  useEffect(() => {
    if (enabled) return;
    clearIdle();
    resetBurst();
  }, [enabled, clearIdle, resetBurst]);

  useEffect(() => clearIdle, [clearIdle]);

  const onChange = useCallback(
    (e: ChangeEvent<HTMLInputElement>) => {
      setValue(e.target.value);
      // Only reached for edits the keydown handler let through — Backspace,
      // paste, cut. None of those are part of a scan.
      clearIdle();
      resetBurst();
    },
    [setValue, clearIdle, resetBurst],
  );

  const onKeyDown = useCallback(
    (e: KeyboardEvent<HTMLInputElement>) => {
      if (!enabled || e.nativeEvent.isComposing) return;

      if (e.key === "Enter") {
        // Never let the scanner's Enter reach a form this input sits in: the
        // caller decides what a finished scan means, not implicit submission.
        e.preventDefault();
        e.stopPropagation();
        clearIdle();
        resetBurst();
        onScanRef.current(valueRef.current);
        return;
      }

      // Leave shortcuts alone — Ctrl+V, Ctrl+A, and so on.
      if (e.ctrlKey || e.metaKey || e.altKey) return;

      const ch = charForKey(e.code, e.shiftKey);
      if (ch === null) {
        // A key with no US equivalent that would still type something under the
        // active layout. Swallow it, so nothing layout-dependent slips in.
        if (e.key.length === 1) e.preventDefault();
        return;
      }

      e.preventDefault();
      const el = e.currentTarget;
      const current = valueRef.current;
      const start = el.selectionStart ?? current.length;
      const end = el.selectionEnd ?? start;
      pendingCaret.current = { el, pos: start + 1 };
      setValue(current.slice(0, start) + ch + current.slice(end));

      // e.timeStamp is when the key was pressed, not when this handler got to
      // run. A re-render between keystrokes would otherwise stretch the
      // measured gaps and hide a scanner behind its own rendering cost.
      const at = e.timeStamp;
      const b = burst.current;
      if (b.count === 0 || at - b.last > BURST_BREAK_MS) {
        b.first = at;
        b.count = 0;
      }
      b.last = at;
      b.count += 1;

      clearIdle();
      idleTimer.current = window.setTimeout(() => {
        idleTimer.current = null;
        const { first, last, count } = burst.current;
        resetBurst();
        const avgGap = count > 1 ? (last - first) / (count - 1) : Infinity;
        if (count >= MIN_SCAN_LENGTH && avgGap <= SCANNER_MAX_AVG_GAP_MS) {
          onScanRef.current(valueRef.current);
        }
      }, SCAN_END_IDLE_MS);
    },
    [enabled, setValue, clearIdle, resetBurst],
  );

  return { value, setValue, onChange, onKeyDown };
}
