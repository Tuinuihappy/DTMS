"use client";

import {
  Activity,
  Bell,
  Cpu,
  Gauge,
  Headphones,
  Map,
  ShieldCheck,
  Sparkles,
} from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

const ease = [0.22, 1, 0.36, 1] as const;

export function BentoFeatures() {
  return (
    <section id="features" className="mx-auto max-w-[1240px] px-4 pt-24 sm:px-6 md:pt-32">
      <motion.div
        initial={{ opacity: 0, y: 14 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true, margin: "-15%" }}
        transition={{ duration: 0.6, ease }}
        className="text-center"
      >
        <span className="inline-flex items-center gap-2 rounded-full border border-[var(--color-ink-100)] bg-white/60 px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-600)] backdrop-blur dark:bg-white/[0.05]">
          <Sparkles className="h-3 w-3" strokeWidth={2.2} />
          What&apos;s in the box
        </span>
        <h2 className="mt-5 font-display text-[2.25rem] md:text-[3.25rem] leading-[1.05] tracking-[-0.03em] font-semibold text-[var(--color-ink-900)]">
          Dispatchers love it.{" "}
          <span className="text-[var(--color-ink-500)]">Ops directors stop micromanaging.</span>
        </h2>
        <p className="mt-4 max-w-xl mx-auto text-[15px] leading-relaxed text-[var(--color-ink-500)]">
          Seven surfaces that turn shipment chaos into a controlled funnel —
          from quote acceptance to delivered cargo and everything between.
        </p>
      </motion.div>

      {/* Bento grid */}
      <div className="mt-14 grid grid-cols-12 gap-4 md:gap-5 auto-rows-[minmax(220px,auto)]">
        {/* 1 — Wide hero: Live Fleet Activity */}
        <BentoCard
          className="col-span-12 md:col-span-8 md:row-span-2"
          delay={0}
          icon={<Activity className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="Real-time"
          title="Live fleet activity that actually streams."
          body="Every dispatch, ETA, exception and proof-of-delivery hits the dashboard within 800 ms. No more F5. No more 'where is truck 14?'"
        >
          <FleetVisual />
        </BentoCard>

        {/* 2 — Dispatch Funnel */}
        <BentoCard
          className="col-span-12 md:col-span-4"
          delay={0.06}
          icon={<Gauge className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="Pipeline"
          title="Dispatch funnel that converts."
          body="Request → Quoted → Confirmed → Dispatched → Delivered. Each step measured, each drop-off caught."
        >
          <FunnelVisual />
        </BentoCard>

        {/* 3 — Driver Performance */}
        <BentoCard
          className="col-span-12 md:col-span-4"
          delay={0.12}
          icon={<ShieldCheck className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="Performance"
          title="Driver scoreboard."
          body="On-time %, fuel deltas, safety events. Reward the right behaviour."
          tone="pastel-mint"
        >
          <ScoreboardVisual />
        </BentoCard>

        {/* 4 — Live map */}
        <BentoCard
          className="col-span-12 md:col-span-8"
          delay={0.18}
          icon={<Map className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="Geo"
          title="Routes you can read at a glance."
          body="Heat-mapped corridors, dynamic re-routing on incident, geo-fence triggers — without the GIS PhD."
          tone="pastel-sky"
        >
          <MapVisual />
        </BentoCard>

        {/* 5 — Comms */}
        <BentoCard
          className="col-span-12 md:col-span-4"
          delay={0.24}
          icon={<Headphones className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="Comms"
          title="Push-to-talk on every shift."
          body="Dispatcher ↔ driver voice messages. Transcribed. Searchable."
          tone="pastel-peach"
        >
          <CommsVisual />
        </BentoCard>

        {/* 6 — Alerts */}
        <BentoCard
          className="col-span-12 md:col-span-4"
          delay={0.30}
          icon={<Bell className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="Alerts"
          title="SLA misses caught before they happen."
          body="Predictive ETA drift, fatigue alarms, dock backup signals."
          tone="pastel-lavender"
        >
          <AlertsVisual />
        </BentoCard>

        {/* 7 — Integrations */}
        <BentoCard
          className="col-span-12 md:col-span-4"
          delay={0.36}
          icon={<Cpu className="h-4 w-4" strokeWidth={2.2} />}
          eyebrow="API + Hardware"
          title="Plugs into the boring stuff."
          body="ELDs, fuel cards, dock doors, ERP. REST + Webhooks + EDI."
        >
          <IntegrationsVisual />
        </BentoCard>
      </div>
    </section>
  );
}

/* -------------------------------------------------------------------------- */
/*  BentoCard primitive                                                        */
/* -------------------------------------------------------------------------- */
function BentoCard({
  className,
  delay = 0,
  icon,
  eyebrow,
  title,
  body,
  children,
  tone = "default",
}: {
  className?: string;
  delay?: number;
  icon: React.ReactNode;
  eyebrow: string;
  title: string;
  body: string;
  children?: React.ReactNode;
  tone?:
    | "default"
    | "strong"
    | "pastel-sky"
    | "pastel-lavender"
    | "pastel-peach"
    | "pastel-mint";
}) {
  const pastel = tone.startsWith("pastel");
  return (
    <GlassCard
      variant={tone}
      initial={{ opacity: 0, y: 22 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: "-10%" }}
      transition={{ duration: 0.6, delay, ease }}
      className={cn("p-6 md:p-7 flex flex-col", className)}
    >
      <div className="flex items-center gap-2.5">
        <span className="grid h-9 w-9 place-items-center rounded-[12px] bg-white/70 text-[var(--color-ink-800)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_10px_-4px_rgba(15,23,42,0.12)] dark:bg-white/[0.08] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.1),0_4px_10px_-4px_rgba(0,0,0,0.4)]">
          {icon}
        </span>
        <span
          className={cn(
            "text-[10.5px] uppercase tracking-[0.14em] font-semibold",
            pastel ? "opacity-70" : "text-[var(--color-ink-400)]",
          )}
        >
          {eyebrow}
        </span>
      </div>

      <h3
        className={cn(
          "mt-5 font-display text-[1.35rem] md:text-[1.55rem] leading-[1.15] tracking-[-0.02em] font-semibold",
          pastel ? "" : "text-[var(--color-ink-900)]",
        )}
      >
        {title}
      </h3>
      <p
        className={cn(
          "mt-2 text-[13.5px] leading-relaxed",
          pastel ? "opacity-75" : "text-[var(--color-ink-500)]",
        )}
      >
        {body}
      </p>

      {children && <div className="mt-5 flex-1 min-h-0">{children}</div>}
    </GlassCard>
  );
}

/* -------------------------------------------------------------------------- */
/*  Per-card visuals                                                           */
/* -------------------------------------------------------------------------- */

function FleetVisual() {
  const bars = [40, 55, 70, 50, 80, 95, 65, 75];
  return (
    <div className="relative h-full min-h-[160px] rounded-[20px] bg-gradient-to-br from-white/40 to-white/10 p-5 dark:from-white/[0.04] dark:to-white/[0.02] overflow-hidden">
      <div className="absolute top-3 right-3 inline-flex items-center gap-1.5 rounded-full bg-white/80 px-2.5 py-1 text-[10.5px] font-semibold dark:bg-white/[0.08]">
        <StatusPulse tone="success" />
        Streaming
      </div>
      <div className="absolute inset-x-5 bottom-5 flex items-end gap-2 h-[70%]">
        {bars.map((h, i) => (
          <motion.div
            key={i}
            initial={{ scaleY: 0 }}
            whileInView={{ scaleY: 1 }}
            viewport={{ once: true }}
            transition={{ duration: 0.7, delay: 0.4 + i * 0.05, ease }}
            className="flex-1 rounded-full bg-[var(--color-brand-900)] origin-bottom"
            style={{ height: `${h}%`, opacity: 0.4 + (h / 100) * 0.6 }}
          />
        ))}
      </div>
      {/* floating mini-card */}
      <div className="absolute top-1/2 left-6 -translate-y-1/2 rounded-2xl bg-white/90 px-3 py-2 shadow-[0_12px_30px_-10px_rgba(15,23,42,0.18)] dark:bg-[var(--color-surface-soft)]/95">
        <div className="text-[10px] uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
          Truck 14 · ETA
        </div>
        <div className="font-mono text-[16px] font-semibold text-[var(--color-ink-900)]">
          14:32
        </div>
        <div className="mt-0.5 inline-flex items-center gap-1 text-[10px] font-semibold text-[var(--color-success)]">
          <StatusPulse tone="success" />
          On corridor
        </div>
      </div>
    </div>
  );
}

function FunnelVisual() {
  const steps = [
    { label: "Req", val: 312 },
    { label: "Qte", val: 248 },
    { label: "Conf", val: 196 },
    { label: "Disp", val: 174 },
    { label: "Done", val: 158 },
  ];
  const max = 312;
  return (
    <div className="grid grid-cols-5 gap-1 items-end h-full min-h-[140px]">
      {steps.map((s, i) => {
        const h = (s.val / max) * 100;
        return (
          <div key={s.label} className="flex flex-col items-center gap-1.5 h-full justify-end">
            <motion.div
              initial={{ height: 0 }}
              whileInView={{ height: `${h}%` }}
              viewport={{ once: true }}
              transition={{ duration: 0.8, delay: 0.2 + i * 0.07, ease }}
              className="w-full rounded-t-md rounded-b-sm bg-[var(--color-brand-900)]"
              style={{ opacity: 0.35 + (s.val / max) * 0.65 }}
            />
            <span className="font-mono text-[9.5px] text-[var(--color-ink-500)]">
              {s.val}
            </span>
            <span className="text-[9px] uppercase tracking-wider font-semibold text-[var(--color-ink-400)]">
              {s.label}
            </span>
          </div>
        );
      })}
    </div>
  );
}

function ScoreboardVisual() {
  return (
    <ul className="space-y-2">
      {[
        { rank: "01", name: "Niran S.", score: 982 },
        { rank: "02", name: "Marisa P.", score: 941 },
        { rank: "03", name: "Kenji O.", score: 918 },
      ].map((d, i) => (
        <motion.li
          key={d.rank}
          initial={{ opacity: 0, x: -8 }}
          whileInView={{ opacity: 1, x: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.4, delay: 0.3 + i * 0.07 }}
          className="flex items-center gap-2.5 rounded-xl bg-white/55 px-3 py-2 backdrop-blur dark:bg-white/[0.06]"
        >
          <span className="font-mono text-[10px] font-semibold opacity-60">{d.rank}</span>
          <span className="flex-1 text-[12.5px] font-semibold">{d.name}</span>
          <span className="font-mono text-[12.5px] font-bold">{d.score}</span>
        </motion.li>
      ))}
    </ul>
  );
}

function MapVisual() {
  return (
    <div className="relative h-full min-h-[180px] rounded-[20px] bg-gradient-to-br from-white/30 to-white/10 overflow-hidden dark:from-white/[0.03] dark:to-white/[0.01]">
      <svg viewBox="0 0 320 180" className="absolute inset-0 h-full w-full">
        {/* dotted grid */}
        <defs>
          <pattern id="map-dots" width="14" height="14" patternUnits="userSpaceOnUse">
            <circle cx="2" cy="2" r="0.6" fill="currentColor" opacity="0.25" />
          </pattern>
        </defs>
        <rect width="320" height="180" fill="url(#map-dots)" />
        {/* route paths */}
        <motion.path
          d="M30,140 Q90,40 160,90 T290,50"
          fill="none"
          stroke="var(--color-brand-900)"
          strokeWidth="2"
          strokeDasharray="4 4"
          initial={{ pathLength: 0 }}
          whileInView={{ pathLength: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 1.4, ease }}
        />
        <motion.path
          d="M40,30 Q120,100 200,60 T300,140"
          fill="none"
          stroke="var(--color-amber)"
          strokeWidth="2"
          strokeDasharray="4 4"
          initial={{ pathLength: 0 }}
          whileInView={{ pathLength: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 1.6, delay: 0.2, ease }}
        />
        {/* nodes */}
        {[
          [30, 140],
          [160, 90],
          [290, 50],
          [200, 60],
          [300, 140],
        ].map(([x, y], i) => (
          <motion.circle
            key={i}
            cx={x}
            cy={y}
            r="4"
            fill="var(--color-brand-900)"
            initial={{ scale: 0 }}
            whileInView={{ scale: 1 }}
            viewport={{ once: true }}
            transition={{ duration: 0.4, delay: 1 + i * 0.1, ease: [0.34, 1.56, 0.64, 1] }}
          />
        ))}
      </svg>
      <div className="absolute bottom-3 left-3 right-3 flex items-center justify-between rounded-full bg-white/85 px-3 py-1.5 text-[10.5px] font-semibold backdrop-blur dark:bg-white/[0.08]">
        <span>Eastern Seaboard · 4 active</span>
        <span className="font-mono text-[var(--color-success)]">96.4% OTD</span>
      </div>
    </div>
  );
}

function CommsVisual() {
  return (
    <div className="rounded-2xl bg-white/55 p-3 backdrop-blur dark:bg-white/[0.06]">
      <div className="flex items-center gap-2">
        <span className="grid h-8 w-8 place-items-center rounded-full bg-[var(--color-amber)] text-[var(--color-brand-900)]">
          <Headphones className="h-4 w-4" strokeWidth={2.4} />
        </span>
        <div className="flex-1 flex items-center gap-[2px] h-6">
          {[0.4, 0.7, 0.5, 0.9, 0.6, 0.85, 0.45, 0.7, 0.5, 0.8, 0.55, 0.65, 0.4].map((v, i) => (
            <motion.span
              key={i}
              initial={{ scaleY: 0 }}
              whileInView={{ scaleY: 1 }}
              viewport={{ once: true }}
              transition={{ duration: 0.4, delay: 0.3 + i * 0.04 }}
              className="w-[3px] rounded-full bg-current origin-center"
              style={{ height: `${v * 100}%`, opacity: 0.6 + v * 0.4 }}
            />
          ))}
        </div>
      </div>
      <div className="mt-2 font-mono text-[10px] opacity-60">00:41 · Niran S.</div>
    </div>
  );
}

function AlertsVisual() {
  const alerts = [
    { label: "Driver 7 fatigue", tone: "coral" as const },
    { label: "Dock 3 backup", tone: "amber" as const },
    { label: "ETA drift +18m", tone: "amber" as const },
  ];
  return (
    <ul className="space-y-2">
      {alerts.map((a, i) => (
        <motion.li
          key={a.label}
          initial={{ opacity: 0, x: -8 }}
          whileInView={{ opacity: 1, x: 0 }}
          viewport={{ once: true }}
          transition={{ duration: 0.4, delay: 0.3 + i * 0.07 }}
          className="flex items-center gap-2.5 rounded-xl bg-white/55 px-3 py-2 backdrop-blur dark:bg-white/[0.06]"
        >
          <StatusPulse tone={a.tone} />
          <span className="text-[12px] font-medium">{a.label}</span>
        </motion.li>
      ))}
    </ul>
  );
}

function IntegrationsVisual() {
  const logos = ["SAP", "Geotab", "Samsara", "Oracle", "Twilio", "Slack"];
  return (
    <div className="grid grid-cols-3 gap-2">
      {logos.map((l, i) => (
        <motion.div
          key={l}
          initial={{ opacity: 0, scale: 0.85 }}
          whileInView={{ opacity: 1, scale: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.35, delay: 0.25 + i * 0.05, ease: [0.34, 1.56, 0.64, 1] }}
          className="grid h-12 place-items-center rounded-xl bg-white/55 backdrop-blur dark:bg-white/[0.06]"
        >
          <span className="font-display text-[12.5px] font-semibold tracking-tight">
            {l}
          </span>
        </motion.div>
      ))}
    </div>
  );
}
