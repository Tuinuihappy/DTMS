"use client";

import { Fingerprint, Key, Laptop, Smartphone } from "lucide-react";
import { motion } from "motion/react";
import { GlassCard } from "@/components/primitives/glass-card";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

const sessions = [
  {
    icon: <Laptop className="h-4 w-4" strokeWidth={2.2} />,
    label: "Control Room · Workstation 04",
    detail: "Chrome · 192.168.4.18 · Laem Chabang",
    time: "Active now",
    current: true,
  },
  {
    icon: <Smartphone className="h-4 w-4" strokeWidth={2.2} />,
    label: "iPhone 15 Pro · Driver App",
    detail: "Last seen 1h ago · BKK",
    time: "1h ago",
    current: false,
  },
  {
    icon: <Laptop className="h-4 w-4" strokeWidth={2.2} />,
    label: "MacBook Pro · Home",
    detail: "Safari · Bangkok",
    time: "Yesterday",
    current: false,
  },
];

export function TabSecurity() {
  return (
    <div className="grid grid-cols-12 gap-5">
      <GlassCard
        variant="default"
        className="col-span-12 lg:col-span-6 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.05, ease: EASE }}
      >
        <SectionLabel>Sign-in security</SectionLabel>

        <div className="mt-5 space-y-3">
          <SettingRow
            icon={<Key className="h-4 w-4" strokeWidth={2.2} />}
            title="Password"
            detail="Last changed 41 days ago"
            cta="Change"
          />
          <SettingRow
            icon={<Fingerprint className="h-4 w-4" strokeWidth={2.2} />}
            title="Two-factor authentication"
            detail="Authenticator app · enrolled"
            cta="Manage"
            on
          />
          <SettingRow
            icon={<Key className="h-4 w-4" strokeWidth={2.2} />}
            title="Recovery codes"
            detail="8 of 10 codes remaining"
            cta="Regenerate"
          />
        </div>
      </GlassCard>

      <GlassCard
        variant="default"
        className="col-span-12 lg:col-span-6 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.12, ease: EASE }}
      >
        <div className="flex items-center justify-between">
          <SectionLabel>Active sessions</SectionLabel>
          <button className="text-[11.5px] font-semibold text-[var(--color-coral)] hover:underline cursor-pointer">
            Sign out everywhere
          </button>
        </div>

        <ul className="mt-5 space-y-2.5">
          {sessions.map((s, i) => (
            <motion.li
              key={i}
              initial={{ opacity: 0, x: -8 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.45, delay: 0.18 + i * 0.05, ease: EASE }}
              className="flex items-center gap-3 rounded-2xl bg-white/55 px-4 py-3.5 dark:bg-white/[0.04]"
            >
              <span className="grid h-9 w-9 place-items-center rounded-full bg-gradient-to-br from-[var(--color-ink-100)] to-[#cfd6e6] text-[var(--color-ink-700)] shadow-[inset_0_1px_0_rgba(255,255,255,0.7)] dark:to-[#3a4870] dark:text-[var(--color-ink-700)]">
                {s.icon}
              </span>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <p className="truncate text-[13.5px] font-semibold text-[var(--color-ink-900)]">{s.label}</p>
                  {s.current && (
                    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--color-success)]">
                      <span className="h-1.5 w-1.5 rounded-full bg-[var(--color-success)]" />
                      This device
                    </span>
                  )}
                </div>
                <p className="mt-0.5 truncate text-[11.5px] font-mono text-[var(--color-ink-500)]">
                  {s.detail}
                </p>
              </div>
              <span className="font-mono text-[11px] text-[var(--color-ink-500)] shrink-0">{s.time}</span>
            </motion.li>
          ))}
        </ul>
      </GlassCard>
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

function SettingRow({
  icon,
  title,
  detail,
  cta,
  on,
}: {
  icon: React.ReactNode;
  title: string;
  detail: string;
  cta: string;
  on?: boolean;
}) {
  return (
    <div className="flex items-center gap-3 rounded-2xl bg-white/55 px-4 py-3.5 dark:bg-white/[0.04]">
      <span className="grid h-9 w-9 place-items-center rounded-full bg-[var(--color-ink-50)] text-[var(--color-ink-700)] dark:bg-white/[0.06]">
        {icon}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <p className="truncate text-[13.5px] font-semibold text-[var(--color-ink-900)]">{title}</p>
          {on && (
            <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--color-success)]">
              On
            </span>
          )}
        </div>
        <p className="mt-0.5 truncate text-[11.5px] text-[var(--color-ink-500)]">{detail}</p>
      </div>
      <button className="rounded-full border border-[var(--color-ink-100)] bg-white px-3 py-1.5 text-[11.5px] font-medium text-[var(--color-ink-700)] transition-colors hover:bg-[var(--color-ink-50)] cursor-pointer dark:border-white/10 dark:bg-white/[0.06] dark:hover:bg-white/[0.12]">
        {cta}
      </button>
    </div>
  );
}
