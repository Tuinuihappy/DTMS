"use client";

import { Languages, Mail, MessageSquare, Moon, Smartphone, Sun, Volume2 } from "lucide-react";
import { motion } from "motion/react";
import { useTheme } from "next-themes";
import { useEffect, useState } from "react";
import { GlassCard } from "@/components/primitives/glass-card";
import { cn } from "@/lib/utils";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

export function TabPreferences() {
  return (
    <div className="grid grid-cols-12 gap-5">
      {/* Appearance */}
      <GlassCard
        variant="default"
        className="col-span-12 lg:col-span-6 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.05, ease: EASE }}
      >
        <SectionLabel>Appearance</SectionLabel>
        <h3 className="mt-2 font-display text-[1.2rem] font-semibold tracking-[-0.02em] text-[var(--color-ink-900)]">
          Theme
        </h3>
        <p className="mt-1 text-[12.5px] text-[var(--color-ink-500)]">
          The control room glows the way you do.
        </p>
        <ThemeSwitcher />

        <div className="mt-6 inset-divider" />

        <div className="mt-5 flex items-center gap-3 rounded-2xl bg-white/55 px-4 py-3.5 dark:bg-white/[0.04]">
          <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-ink-50)] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
            <Languages className="h-4 w-4" strokeWidth={2.2} />
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-[13.5px] font-semibold text-[var(--color-ink-900)]">Language</p>
            <p className="mt-0.5 text-[11.5px] text-[var(--color-ink-500)]">
              English (US) · auto-detected from device
            </p>
          </div>
          <select
            defaultValue="en-US"
            className="rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[12px] font-medium text-[var(--color-ink-700)] outline-none cursor-pointer dark:border-white/10 dark:bg-white/[0.06]"
          >
            <option value="en-US">English</option>
            <option value="th-TH">ไทย</option>
            <option value="ja-JP">日本語</option>
          </select>
        </div>
      </GlassCard>

      {/* Notifications */}
      <GlassCard
        variant="default"
        className="col-span-12 lg:col-span-6 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.12, ease: EASE }}
      >
        <SectionLabel>Notifications</SectionLabel>
        <h3 className="mt-2 font-display text-[1.2rem] font-semibold tracking-[-0.02em] text-[var(--color-ink-900)]">
          What reaches you
        </h3>
        <p className="mt-1 text-[12.5px] text-[var(--color-ink-500)]">
          Decide which channels surface dispatch events.
        </p>

        <div className="mt-5 space-y-2.5">
          <ToggleRow
            icon={<Mail className="h-4 w-4" strokeWidth={2.2} />}
            title="Email digest"
            detail="Daily 06:00 summary of the prior shift"
            defaultOn
          />
          <ToggleRow
            icon={<Smartphone className="h-4 w-4" strokeWidth={2.2} />}
            title="Push to driver app"
            detail="Critical alerts only · escalates after 90s"
            defaultOn
          />
          <ToggleRow
            icon={<MessageSquare className="h-4 w-4" strokeWidth={2.2} />}
            title="SMS for hazmat events"
            detail="Class 3 / 6 / 7 incidents"
            defaultOn={false}
          />
          <ToggleRow
            icon={<Volume2 className="h-4 w-4" strokeWidth={2.2} />}
            title="Control-room audio cue"
            detail="Soft chime when a shipment slips SLA"
            defaultOn
          />
        </div>
      </GlassCard>
    </div>
  );
}

function ThemeSwitcher() {
  const { theme, resolvedTheme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);
  const current = mounted ? (theme === "system" ? "system" : (resolvedTheme ?? "light")) : "light";

  const opts = [
    { key: "light", label: "Light", icon: <Sun className="h-3.5 w-3.5" strokeWidth={2.2} /> },
    { key: "dark", label: "Dark", icon: <Moon className="h-3.5 w-3.5" strokeWidth={2.2} /> },
    { key: "system", label: "System", icon: <Smartphone className="h-3.5 w-3.5" strokeWidth={2.2} /> },
  ] as const;

  return (
    <div className="mt-4 inline-flex items-center gap-1 rounded-full border border-[var(--color-ink-100)] bg-white/70 p-1 dark:border-white/10 dark:bg-white/[0.04]">
      {opts.map((o) => {
        const active = current === o.key || (o.key === "system" && theme === "system");
        return (
          <button
            key={o.key}
            type="button"
            onClick={() => setTheme(o.key)}
            className={cn(
              "relative inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-[12px] font-medium transition-colors cursor-pointer",
              active
                ? "text-[var(--color-ink-900)]"
                : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-800)]",
            )}
          >
            {active && (
              <motion.span
                layoutId="theme-pill"
                transition={{ type: "spring", stiffness: 360, damping: 30 }}
                className="absolute inset-0 rounded-full bg-white shadow-[inset_0_1px_0_rgba(255,255,255,1),0_4px_10px_-4px_rgba(15,23,42,0.18)] dark:bg-white/[0.08]"
              />
            )}
            <span className="relative inline-flex items-center gap-1.5">
              {o.icon}
              {o.label}
            </span>
          </button>
        );
      })}
    </div>
  );
}

function ToggleRow({
  icon,
  title,
  detail,
  defaultOn,
}: {
  icon: React.ReactNode;
  title: string;
  detail: string;
  defaultOn: boolean;
}) {
  const [on, setOn] = useState(defaultOn);
  return (
    <div className="flex items-center gap-3 rounded-2xl bg-white/55 px-4 py-3.5 dark:bg-white/[0.04]">
      <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-ink-50)] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
        {icon}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-[13.5px] font-semibold text-[var(--color-ink-900)]">{title}</p>
        <p className="mt-0.5 text-[11.5px] text-[var(--color-ink-500)]">{detail}</p>
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={on}
        onClick={() => setOn((v) => !v)}
        className={cn(
          "relative h-6 w-11 rounded-full transition-colors cursor-pointer shrink-0",
          on
            ? "bg-[var(--color-brand-900)] dark:bg-[var(--color-brand-500)]"
            : "bg-[var(--color-ink-200)] dark:bg-white/[0.12]",
        )}
      >
        <motion.span
          animate={{ x: on ? 22 : 2 }}
          transition={{ type: "spring", stiffness: 420, damping: 28 }}
          className="absolute top-1 left-0 h-4 w-4 rounded-full bg-white shadow-[0_2px_4px_rgba(15,23,42,0.2)]"
        />
      </button>
    </div>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <span className="text-[10.5px] font-bold uppercase tracking-[0.18em] text-[var(--color-ink-400)]">
      {children}
    </span>
  );
}
