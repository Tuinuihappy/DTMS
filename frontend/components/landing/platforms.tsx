"use client";

import {
  ArrowDownToLine,
  Chrome,
  Globe,
  MonitorSmartphone,
  Smartphone,
} from "lucide-react";
import { AnimatePresence, motion } from "motion/react";
import { useState } from "react";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

const ease = [0.22, 1, 0.36, 1] as const;

const platforms = [
  {
    key: "web",
    label: "Web",
    icon: <Globe className="h-4 w-4" strokeWidth={2.2} />,
    primary: "Open the web dashboard",
    secondary: "Works in any modern browser · no install",
  },
  {
    key: "ios",
    label: "iOS",
    icon: <Smartphone className="h-4 w-4" strokeWidth={2.2} />,
    primary: "Download for iOS 16+",
    secondary: "Driver app · 18 MB · App Store rated 4.8",
  },
  {
    key: "android",
    label: "Android",
    icon: <Smartphone className="h-4 w-4" strokeWidth={2.2} />,
    primary: "Download for Android 10+",
    secondary: "Driver app · 22 MB · Play Store rated 4.7",
  },
  {
    key: "windows",
    label: "Windows",
    icon: <MonitorSmartphone className="h-4 w-4" strokeWidth={2.2} />,
    primary: "Download dispatch console",
    secondary: "Windows 10+ · 64-bit · 142 MB",
  },
  {
    key: "extension",
    label: "Extension",
    icon: <Chrome className="h-4 w-4" strokeWidth={2.2} />,
    primary: "Add to browser",
    secondary: "Chrome · Edge · Firefox · 2.4 MB",
  },
] as const;

type PlatformKey = (typeof platforms)[number]["key"];

export function Platforms() {
  const [active, setActive] = useState<PlatformKey>("web");
  const cur = platforms.find((p) => p.key === active)!;

  return (
    <section id="platforms" className="mx-auto max-w-[1240px] px-4 pt-24 sm:px-6 md:pt-32">
      <motion.div
        initial={{ opacity: 0, y: 14 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-15%" }}
        transition={{ duration: 0.6, ease }}
        className="text-center"
      >
        <h2 className="font-display text-[2.25rem] md:text-[3.25rem] leading-[1.05] tracking-[-0.03em] font-semibold text-[var(--color-ink-900)]">
          Run TMS{" "}
          <span className="text-[var(--color-ink-500)]">anywhere your team is.</span>
        </h2>
        <p className="mt-4 max-w-xl mx-auto text-[15px] leading-relaxed text-[var(--color-ink-500)]">
          One account, every screen. Drivers in the cab, dispatchers at the
          desk, ops directors on their phone at midnight.
        </p>
      </motion.div>

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-10%" }}
        transition={{ duration: 0.7, delay: 0.15, ease }}
        className="glass glass-edge mt-12 rounded-[var(--radius-2xl)] p-6 md:p-8"
      >
        {/* Tabs */}
        <div className="flex flex-wrap items-center justify-center gap-1.5 rounded-full bg-white/55 p-1.5 backdrop-blur dark:bg-white/[0.06] max-w-fit mx-auto">
          {platforms.map((p) => (
            <button
              key={p.key}
              onClick={() => setActive(p.key)}
              className={cn(
                "inline-flex items-center gap-2 rounded-full px-3.5 py-2 text-[12.5px] font-medium transition-all cursor-pointer",
                active === p.key
                  ? "bg-white text-[var(--color-ink-900)] shadow-[0_4px_10px_-4px_rgba(15,23,42,0.18)] dark:bg-white/[0.14] dark:text-[var(--color-ink-900)]"
                  : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-800)]",
              )}
            >
              {p.icon}
              {p.label}
            </button>
          ))}
        </div>

        {/* Preview frame */}
        <div className="mt-8 grid grid-cols-1 md:grid-cols-12 gap-6 md:gap-8 items-center">
          <div className="md:col-span-5">
            <AnimatePresence mode="wait">
              <motion.div
                key={active}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -10 }}
                transition={{ duration: 0.35, ease }}
              >
                <div className="text-[10.5px] uppercase tracking-[0.14em] font-semibold text-[var(--color-ink-400)]">
                  {cur.label} client
                </div>
                <div className="mt-2 font-display text-[1.75rem] leading-tight tracking-tight font-semibold text-[var(--color-ink-900)]">
                  {cur.primary}
                </div>
                <p className="mt-2 text-[13.5px] text-[var(--color-ink-500)]">
                  {cur.secondary}
                </p>
                <button className="group mt-5 inline-flex items-center gap-2 rounded-full bg-[var(--color-brand-900)] pl-4 pr-2 py-2 text-[13px] font-semibold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_10px_24px_-10px_rgba(14,21,48,0.6)] transition-transform duration-200 hover:-translate-y-0.5 cursor-pointer">
                  <ArrowDownToLine className="h-4 w-4" strokeWidth={2.4} />
                  {cur.label === "Web" ? "Launch" : "Download"}
                  <span className="grid h-7 w-7 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)] font-mono text-[10px]">
                    {cur.label === "Web" ? "GO" : "↓"}
                  </span>
                </button>
                <div className="mt-3 inline-flex items-center gap-1.5 text-[10.5px] font-medium text-[var(--color-ink-500)]">
                  <StatusPulse tone="success" />
                  Requires sign-in
                </div>
              </motion.div>
            </AnimatePresence>
          </div>

          <div className="md:col-span-7">
            <PlatformPreview platform={active} />
          </div>
        </div>
      </motion.div>
    </section>
  );
}

/* -------------------------------------------------------------------------- */
function PlatformPreview({ platform }: { platform: PlatformKey }) {
  return (
    <AnimatePresence mode="wait">
      <motion.div
        key={platform}
        initial={{ opacity: 0, scale: 0.96, y: 14 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.96, y: -10 }}
        transition={{ duration: 0.45, ease }}
        className="relative aspect-[16/10] w-full overflow-hidden rounded-[var(--radius-xl)] bg-gradient-to-br from-[var(--color-canvas-deep)] to-[var(--color-canvas)] p-4 dark:from-[var(--color-surface)] dark:to-[var(--color-canvas)]"
        style={{
          boxShadow:
            "inset 0 1px 0 rgba(255,255,255,0.5), 0 30px 60px -20px rgba(15,23,42,0.2)",
        }}
      >
        {/* Mock window chrome */}
        <div className="absolute top-3 left-4 flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-full bg-[var(--color-coral)]" />
          <span className="h-2.5 w-2.5 rounded-full bg-[var(--color-amber)]" />
          <span className="h-2.5 w-2.5 rounded-full bg-[var(--color-success)]" />
        </div>
        <div className="absolute top-3 left-1/2 -translate-x-1/2 rounded-full bg-white/70 px-3 py-0.5 text-[10px] font-mono text-[var(--color-ink-500)] dark:bg-white/[0.08]">
          tms.app/{platform}
        </div>

        {/* Body */}
        <div className="absolute inset-x-4 top-12 bottom-4 grid grid-cols-3 gap-2">
          <div className="rounded-2xl bg-white/85 p-3 backdrop-blur dark:bg-white/[0.08]">
            <div className="text-[8.5px] uppercase tracking-wider opacity-60">
              Active
            </div>
            <div className="font-mono text-[18px] font-bold mt-0.5">142</div>
            <div className="mt-2 h-1.5 rounded-full bg-[var(--color-brand-900)]/30">
              <div className="h-full w-[78%] rounded-full bg-[var(--color-brand-900)]" />
            </div>
          </div>
          <div className="rounded-2xl bg-white/85 p-3 backdrop-blur dark:bg-white/[0.08]">
            <div className="text-[8.5px] uppercase tracking-wider opacity-60">
              On-time
            </div>
            <div className="font-mono text-[18px] font-bold mt-0.5 text-[var(--color-success)]">
              96.4%
            </div>
            <div className="mt-2 flex items-end gap-0.5 h-3">
              {[0.4, 0.6, 0.5, 0.75, 0.65, 0.85].map((h, i) => (
                <span
                  key={i}
                  className="flex-1 rounded-sm bg-[var(--color-success)]"
                  style={{ height: `${h * 100}%`, opacity: 0.5 + h * 0.5 }}
                />
              ))}
            </div>
          </div>
          <div className="rounded-2xl bg-white/85 p-3 backdrop-blur dark:bg-white/[0.08]">
            <div className="text-[8.5px] uppercase tracking-wider opacity-60">
              Alerts
            </div>
            <div className="font-mono text-[18px] font-bold mt-0.5 text-[var(--color-coral)]">
              3
            </div>
            <div className="mt-2 inline-flex items-center gap-1 text-[9px] font-semibold">
              <StatusPulse tone="coral" />
              live
            </div>
          </div>
          <div className="col-span-3 rounded-2xl bg-white/85 p-3 backdrop-blur dark:bg-white/[0.08]">
            <div className="flex items-center justify-between">
              <div className="text-[10px] uppercase tracking-wider opacity-60 font-semibold">
                Dispatch funnel
              </div>
              <div className="font-mono text-[9.5px] opacity-50">last 7d</div>
            </div>
            <div className="mt-2 flex items-end gap-1 h-12">
              {[312, 248, 196, 174, 158].map((v, i) => (
                <div key={i} className="flex-1 flex flex-col items-center gap-1">
                  <div
                    className="w-full rounded-t bg-[var(--color-brand-900)]"
                    style={{
                      height: `${(v / 312) * 100}%`,
                      opacity: 0.4 + (v / 312) * 0.6,
                    }}
                  />
                  <span className="font-mono text-[7.5px] opacity-50">{v}</span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </motion.div>
    </AnimatePresence>
  );
}
