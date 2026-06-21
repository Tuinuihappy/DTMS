"use client";

import { Award, Mail, MapPin, Phone, Truck, Wrench } from "lucide-react";
import { motion } from "motion/react";
import { useProfileData } from "@/components/profile/profile-context";
import { GlassCard } from "@/components/primitives/glass-card";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

const certs = [
  { name: "Class A CDL", expires: "2027-08-14", tone: "success" as const },
  { name: "Hazmat Endorsement", expires: "2026-11-02", tone: "amber" as const },
  { name: "Tanker Endorsement", expires: "2028-03-21", tone: "success" as const },
  { name: "ADR · Dangerous Goods", expires: "2026-06-19", tone: "coral" as const },
];

const vehicles = [
  { plate: "TMS · 8842", model: "Volvo FH16 750", status: "Assigned", km: "182,420 km" },
  { plate: "TMS · 5519", model: "Scania R 730", status: "On standby", km: "94,160 km" },
];

export function TabOverview() {
  const profile = useProfileData();
  const live = profile.status === "ready" ? profile.data : null;
  const email = live?.email || "—";

  return (
    <div className="grid grid-cols-12 gap-5">
      {/* About */}
      <GlassCard
        variant="default"
        className="col-span-12 lg:col-span-7 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.05, ease: EASE }}
      >
        <SectionLabel>About</SectionLabel>
        <h3 className="mt-3 font-display text-[1.4rem] font-semibold tracking-[-0.02em] text-[var(--color-ink-900)]">
          Senior operator, Eastern Seaboard
        </h3>
        <p className="mt-3 text-[14.5px] leading-[1.65] text-[var(--color-ink-600)]">
          Eight years dispatching for industrial logistics across the Eastern corridor. Specializes
          in cold-chain and hazmat consignments, with a perfect record across the last 412 runs.
          Currently leads the Sector 04 night-shift rotation.
        </p>

        <div className="mt-6 grid grid-cols-1 gap-3 sm:grid-cols-2">
          <ContactRow icon={<Mail className="h-3.5 w-3.5" strokeWidth={2.2} />} label="Email" value={email} />
          <ContactRow icon={<Phone className="h-3.5 w-3.5" strokeWidth={2.2} />} label="Direct" value="+66 2 555 0142" />
          <ContactRow icon={<MapPin className="h-3.5 w-3.5" strokeWidth={2.2} />} label="Depot" value="Laem Chabang · DC-04" />
          <ContactRow icon={<Wrench className="h-3.5 w-3.5" strokeWidth={2.2} />} label="Shift" value="Nights · 22:00 – 06:00" />
        </div>
      </GlassCard>

      {/* Certifications */}
      <GlassCard
        variant="default"
        className="col-span-12 lg:col-span-5 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.12, ease: EASE }}
      >
        <div className="flex items-center justify-between">
          <SectionLabel>Certifications</SectionLabel>
          <span className="inline-flex items-center gap-1 rounded-full bg-[var(--color-success-soft)] px-2 py-0.5 text-[10.5px] font-semibold text-[var(--color-success)]">
            <Award className="h-2.5 w-2.5" strokeWidth={2.6} />
            4 active
          </span>
        </div>

        <ul className="mt-4 space-y-2.5">
          {certs.map((c, i) => (
            <motion.li
              key={c.name}
              initial={{ opacity: 0, x: -8 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ duration: 0.45, delay: 0.18 + i * 0.05, ease: EASE }}
              className="flex items-center justify-between rounded-2xl bg-white/55 px-4 py-3 dark:bg-white/[0.04]"
            >
              <div className="min-w-0">
                <div className="truncate text-[13.5px] font-semibold text-[var(--color-ink-900)]">
                  {c.name}
                </div>
                <div className="mt-0.5 text-[11.5px] font-mono text-[var(--color-ink-500)]">
                  Expires {c.expires}
                </div>
              </div>
              <span
                className={
                  c.tone === "success"
                    ? "rounded-full bg-[var(--color-success-soft)] px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-success)]"
                    : c.tone === "amber"
                      ? "rounded-full bg-[var(--color-amber-soft)] px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-amber)]"
                      : "rounded-full bg-[var(--color-coral-soft)] px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-[0.08em] text-[var(--color-coral)]"
                }
              >
                {c.tone === "coral" ? "Renew soon" : c.tone === "amber" ? "Due 6mo" : "Valid"}
              </span>
            </motion.li>
          ))}
        </ul>
      </GlassCard>

      {/* Vehicle assignments */}
      <GlassCard
        variant="default"
        className="col-span-12 p-7"
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.18, ease: EASE }}
      >
        <SectionLabel>Vehicle assignments</SectionLabel>

        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          {vehicles.map((v, i) => (
            <motion.div
              key={v.plate}
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.45, delay: 0.24 + i * 0.06, ease: EASE }}
              className="relative overflow-hidden rounded-[var(--radius-lg)] border border-[var(--color-ink-100)]/70 bg-white/55 p-5 dark:border-white/[0.06] dark:bg-white/[0.04]"
            >
              <div className="flex items-start gap-4">
                <span className="grid h-12 w-12 place-items-center rounded-2xl bg-gradient-to-br from-[var(--color-pastel-peach)] to-[#fcb98a] text-[var(--color-pastel-peach-ink)] shadow-[inset_0_1px_0_rgba(255,255,255,0.6)]">
                  <Truck className="h-5 w-5" strokeWidth={2.2} />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-[12.5px] font-semibold tracking-tight text-[var(--color-ink-900)]">
                      {v.plate}
                    </span>
                    <span
                      className={
                        v.status === "Assigned"
                          ? "rounded-full bg-[var(--color-success-soft)] px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--color-success)]"
                          : "rounded-full bg-[var(--color-ink-100)] px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.08em] text-[var(--color-ink-600)] dark:bg-white/[0.08]"
                      }
                    >
                      {v.status}
                    </span>
                  </div>
                  <p className="mt-1 text-[14px] font-semibold text-[var(--color-ink-800)]">{v.model}</p>
                  <p className="mt-0.5 text-[11.5px] font-mono text-[var(--color-ink-500)]">
                    {v.km}
                  </p>
                </div>
              </div>

              {/* Decorative odometer bar */}
              <div className="mt-4 h-1 rounded-full bg-[var(--color-ink-100)] dark:bg-white/[0.06]">
                <motion.div
                  initial={{ width: 0 }}
                  animate={{ width: i === 0 ? "76%" : "42%" }}
                  transition={{ duration: 0.9, delay: 0.4 + i * 0.06, ease: EASE }}
                  className="h-full rounded-full bg-gradient-to-r from-[var(--color-coral)] to-[var(--color-brand-500)]"
                />
              </div>
            </motion.div>
          ))}
        </div>
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

function ContactRow({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div className="flex items-center gap-3 rounded-2xl bg-white/55 px-4 py-3 dark:bg-white/[0.04]">
      <span className="grid h-8 w-8 place-items-center rounded-full bg-[var(--color-ink-50)] text-[var(--color-ink-600)] dark:bg-white/[0.06]">
        {icon}
      </span>
      <div className="min-w-0">
        <div className="text-[10.5px] uppercase tracking-[0.12em] font-medium text-[var(--color-ink-400)]">
          {label}
        </div>
        <div className="truncate text-[13px] font-medium text-[var(--color-ink-800)]">
          {value}
        </div>
      </div>
    </div>
  );
}
