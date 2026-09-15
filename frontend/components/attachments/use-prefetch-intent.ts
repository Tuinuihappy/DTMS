"use client";

import { useEffect, useRef } from "react";
import { preloadImage } from "@/lib/image-preload";

// Long enough that sweeping the pointer down a table does not download every
// photo it crosses; short enough that a pointer on its way to a click has
// started the download well before the click lands.
const INTENT_MS = 60;

/**
 * Handlers that start loading an image once the pointer (or keyboard focus)
 * settles on something that would open it. By the time the click arrives the
 * bytes are usually there, and the viewer opens straight onto a sharp picture.
 */
export function usePrefetchIntent(src: string | null) {
  const timer = useRef<number | null>(null);

  const cancel = () => {
    if (timer.current !== null) {
      window.clearTimeout(timer.current);
      timer.current = null;
    }
  };

  useEffect(() => cancel, []);

  const start = () => {
    if (!src || timer.current !== null) return;
    timer.current = window.setTimeout(() => {
      timer.current = null;
      void preloadImage(src);
    }, INTENT_MS);
  };

  return { onPointerEnter: start, onPointerLeave: cancel, onFocus: start, onBlur: cancel };
}
