"use client";

import { useEffect, useMemo, useState } from "react";
import { motion } from "motion/react";
import { Activity, Layers, Map as MapIcon, Radio } from "lucide-react";

import { NumberTicker } from "@/components/primitives/number-ticker";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";
import {
  getStations,
  listMaps,
  type MapSummaryDto,
  type StationDto,
} from "@/lib/api/facility";
import { useRobotPositions } from "@/components/facility/robot-layer";

const ease = [0.22, 1, 0.36, 1] as const;

/* -------------------------------------------------------------------------- */
/* FleetPulseStrip — a slim "live ops" band that sits between the hero and    */
/* the bento section. Reads real numbers from the same endpoints the         */
/* facility module uses (listMaps + getStations + robot poll on primary      */
/* map) so the home page genuinely feels like a control deck, not a mockup.  */
/* -------------------------------------------------------------------------- */
export function FleetPulseStrip() {
  const [maps, setMaps] = useState<MapSummaryDto[]>([]);
  const [stations, setStations] = useState<StationDto[]>([]);
  const [, setError] = useState<string | null>(null);

  // One-shot fetch of maps + ALL stations. Stations endpoint isn't realtime;
  // we just need the counts. The "live" feel comes from the robot poll below.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await listMaps();
        if (cancelled) return;
        setMaps(list);
        const all = await getStations({ includeInactive: true });
        if (cancelled) return;
        setStations(all);
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : "Failed to load");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const primaryMapId = maps[0]?.id ?? null;
  const { positions: robots, lastTickMs } = useRobotPositions(primaryMapId);

  // Re-render every 30 s so the freshness counter doesn't go stale.
  const [, forceTick] = useState(0);
  useEffect(() => {
    const t = window.setInterval(() => forceTick((n) => (n + 1) % 1_000), 30_000);
    return () => window.clearInterval(t);
  }, []);

  const stats = useMemo(() => {
    return {
      maps: maps.length,
      stations: stations.filter((s) => s.isActive).length,
      robots: robots.length,
    };
  }, [maps, stations, robots]);

  const tiles = [
    {
      label: "Sites tracked",
      hint: "Live facility maps",
      value: stats.maps,
      icon: <MapIcon className="h-3.5 w-3.5" strokeWidth={2.2} />,
    },
    {
      label: "Stations active",
      hint: "Across all sites",
      value: stats.stations,
      icon: <Layers className="h-3.5 w-3.5" strokeWidth={2.2} />,
    },
    {
      label: "Robots online",
      hint: "Streaming now",
      value: stats.robots,
      icon: <Radio className="h-3.5 w-3.5" strokeWidth={2.2} />,
    },
  ];

  const freshness = lastTickMs
    ? `${((performance.now() - lastTickMs) / 1000).toFixed(0)}s ago`
    : "syncing";

  return (
    <section className="mt-16 md:mt-20">
      <motion.div
        initial={{ opacity: 0, y: 16 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-12%" }}
        transition={{ duration: 0.6, ease }}
        className="glass-strong glass-edge relative overflow-hidden rounded-[var(--radius-xl)] px-5 py-4 sm:px-7 sm:py-5"
      >
        {/* Animated sheen along the top edge — Linear-style "alive" detail. */}
        <motion.span
          aria-hidden
          initial={{ x: "-100%" }}
          animate={{ x: "200%" }}
          transition={{ duration: 6, repeat: Infinity, ease: "linear" }}
          className="pointer-events-none absolute -top-px left-0 h-px w-1/3"
          style={{
            background:
              "linear-gradient(90deg, transparent, var(--color-brand-500), transparent)",
          }}
        />

        <div className="flex flex-col gap-5 md:flex-row md:items-center md:justify-between md:gap-8">
          {/* Eyebrow + freshness */}
          <div className="flex items-center justify-between gap-4 md:justify-start">
            <div className="flex items-center gap-2">
              <span className="grid h-9 w-9 place-items-center rounded-[12px] bg-white/70 text-[var(--color-ink-800)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_10px_-4px_rgba(15,23,42,0.12)] dark:bg-white/[0.08]">
                <Activity className="h-4 w-4" strokeWidth={2.2} />
              </span>
              <div>
                <div className="text-[10px] uppercase tracking-[0.14em] font-semibold text-[var(--color-ink-400)]">
                  Live ops
                </div>
                <div className="font-display text-[13.5px] font-semibold tracking-tight text-[var(--color-ink-900)]">
                  Right now, in the deck.
                </div>
              </div>
            </div>
            <div className="inline-flex items-center gap-1.5 rounded-full bg-white/60 px-2.5 py-1 text-[10.5px] font-mono tracking-tight text-[var(--color-ink-600)] dark:bg-white/[0.05]">
              <StatusPulse tone={lastTickMs ? "live" : "amber"} />
              {freshness}
            </div>
          </div>

          {/* Stat tiles — sit in a horizontal row beside the eyebrow on
              desktop, stack as a clean 3-up grid on mobile. */}
          <div className="grid grid-cols-3 gap-3 md:flex md:gap-6">
            {tiles.map((t, i) => (
              <motion.div
                key={t.label}
                initial={{ opacity: 0, y: 8 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true }}
                transition={{ duration: 0.5, delay: 0.1 + i * 0.06, ease }}
                className={cn(
                  "flex flex-col gap-0.5 md:flex-row md:items-baseline md:gap-3",
                  i > 0 && "md:border-l md:border-[var(--color-ink-100)]/70 md:pl-6 dark:md:border-white/[0.06]",
                )}
              >
                <div className="flex items-baseline gap-1">
                  <span className="font-display text-[1.6rem] font-semibold leading-none tracking-tight text-[var(--color-ink-900)] md:text-[1.85rem]">
                    <NumberTicker value={t.value} />
                  </span>
                  <span className="text-[var(--color-ink-400)] md:hidden">{t.icon}</span>
                </div>
                <div className="md:flex md:flex-col">
                  <span className="text-[11px] font-semibold tracking-tight text-[var(--color-ink-700)] md:text-[12px]">
                    {t.label}
                  </span>
                  <span className="hidden md:block text-[10px] uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                    {t.hint}
                  </span>
                </div>
              </motion.div>
            ))}
          </div>
        </div>
      </motion.div>
    </section>
  );
}
