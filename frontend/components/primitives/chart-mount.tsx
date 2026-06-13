"use client";

import { useEffect, useRef, useState } from "react";

/// Holds child render back until ResizeObserver reports the wrapper
/// has non-zero dimensions, so Recharts only mounts when the parent
/// has been laid out.
///
/// <para><b>Caveat:</b> this doesn't fully suppress Recharts'
/// `width(-1) height(-1) ... should be greater than 0` warning —
/// ResponsiveContainer logs it from its own first internal
/// measurement, which happens whether or not the outer wrapper is
/// ready. The wrapper still helps in two ways: charts can't mount
/// against a zero-size parent (avoiding a real layout race), and
/// re-mounts on tab swap don't briefly render a -1-sized chart.
/// Killing the warning entirely would require patching Recharts or
/// swapping ResponsiveContainer for the fixed-pixel API.</para>
export function ChartMount({ children }: { children: React.ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    if (!ref.current) return;
    const el = ref.current;
    // Fast-path: parent already has dimensions (e.g. user re-mounts the
    // same tab quickly). Skip the observer round-trip.
    if (el.clientWidth > 0 && el.clientHeight > 0) {
      setReady(true);
      return;
    }
    const ro = new ResizeObserver((entries) => {
      const rect = entries[0]?.contentRect;
      if (rect && rect.width > 0 && rect.height > 0) {
        setReady(true);
        ro.disconnect();
      }
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // The wrapper div takes the same shape as the child would, so the
  // parent's flex layout doesn't reflow when we swap from null → chart.
  return (
    <div ref={ref} className="h-full w-full">
      {ready ? children : null}
    </div>
  );
}
