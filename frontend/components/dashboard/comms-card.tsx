"use client";

import { Mic, Pause, Play, Radio } from "lucide-react";
import { motion } from "motion/react";
import { useState } from "react";
import { Avatar } from "@/components/primitives/avatar";
import { GlassCard } from "@/components/primitives/glass-card";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

// pseudo-random waveform values
const waveform = [
  0.3, 0.5, 0.7, 0.45, 0.6, 0.9, 0.7, 0.5, 0.65, 0.85, 0.55, 0.35, 0.5, 0.7, 0.95, 0.6, 0.4, 0.55,
  0.75, 0.5, 0.3, 0.6, 0.85, 0.65, 0.45, 0.35, 0.55, 0.75, 0.9, 0.55, 0.4, 0.5, 0.7, 0.6, 0.4,
];

export function CommsCard() {
  const [playing, setPlaying] = useState(false);

  return (
    <GlassCard
      variant="ink"
      className="col-span-12 md:col-span-6 lg:col-span-3 p-6 relative overflow-hidden"
      initial={{ opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.55, delay: 0.75, ease: [0.22, 1, 0.36, 1] }}
    >
      {/* Background glow */}
      <div className="absolute -top-12 -right-10 h-44 w-44 rounded-full bg-[var(--color-brand-500)] opacity-30 blur-3xl" aria-hidden />
      <div className="absolute -bottom-16 -left-12 h-44 w-44 rounded-full bg-[var(--color-amber)] opacity-15 blur-3xl" aria-hidden />

      <div className="relative">
        <div className="flex items-center justify-between">
          <div className="inline-flex items-center gap-2 rounded-full bg-white/8 px-2.5 py-1 text-[10.5px] uppercase tracking-[0.14em] font-semibold text-white/80">
            <Radio className="h-3 w-3" strokeWidth={2.4} />
            Dispatch comms
          </div>
          <span className="inline-flex items-center gap-1.5 text-[10.5px] font-semibold uppercase tracking-[0.12em] text-[var(--color-amber)]">
            <StatusPulse tone="amber" />
            Live
          </span>
        </div>

        <h3 className="mt-5 font-display text-[1.55rem] leading-[1.05] font-semibold tracking-tight text-white">
          Truck&nbsp;14 needs reroute around Bang Pa-in
        </h3>

        <div className="mt-4 flex items-center gap-3">
          <Avatar name="Niran S." hue={18} size="sm" ring />
          <div className="min-w-0">
            <div className="text-[12.5px] font-medium text-white">Niran S.</div>
            <div className="text-[10.5px] text-white/55 font-mono">2 min ago · 00:41</div>
          </div>
        </div>

        {/* Waveform */}
        <div className="mt-5 rounded-2xl bg-white/8 p-3 backdrop-blur">
          <div className="flex items-center gap-3">
            <button
              onClick={() => setPlaying((p) => !p)}
              className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.4),0_6px_14px_-4px_rgba(245,158,11,0.6)] transition-transform hover:scale-105 cursor-pointer"
              aria-label={playing ? "Pause" : "Play"}
            >
              {playing ? <Pause className="h-4 w-4" /> : <Play className="h-4 w-4 ml-0.5" />}
            </button>
            <div className="flex-1 flex items-center justify-between gap-[2px] h-9">
              {waveform.map((v, i) => {
                const isPlayed = playing && i < waveform.length * 0.45;
                return (
                  <motion.span
                    key={i}
                    initial={{ scaleY: 0 }}
                    animate={
                      playing
                        ? {
                            scaleY: [v * 0.5, v, v * 0.7, v],
                          }
                        : { scaleY: v }
                    }
                    transition={
                      playing
                        ? {
                            duration: 0.6 + (i % 4) * 0.1,
                            repeat: Infinity,
                            ease: "easeInOut",
                            delay: i * 0.03,
                          }
                        : { duration: 0.5, delay: 0.85 + i * 0.012 }
                    }
                    className={cn(
                      "w-[3px] rounded-full origin-center",
                      isPlayed ? "bg-[var(--color-amber)]" : "bg-white/35",
                    )}
                    style={{ height: 26 }}
                  />
                );
              })}
            </div>
          </div>
        </div>

        <button className="mt-5 inline-flex w-full items-center justify-center gap-2 rounded-full border border-white/15 bg-white/5 px-4 py-2.5 text-[12.5px] font-medium text-white/90 transition-colors hover:bg-white/10 cursor-pointer">
          <Mic className="h-3.5 w-3.5" strokeWidth={2.2} />
          Reply on channel
        </button>
      </div>
    </GlassCard>
  );
}
