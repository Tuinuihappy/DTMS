"use client";

import {
  AlertTriangle,
  Calendar,
  ChevronLeft,
  Database,
  Moon,
  Plus,
  Send,
  Share2,
  Smartphone,
  Star,
  Sun,
  Upload,
} from "lucide-react";
import { motion } from "motion/react";
import { useState } from "react";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

type RailAction = {
  icon: React.ReactNode;
  label: string;
  badge?: boolean;
};

const actions: RailAction[] = [
  { icon: <ChevronLeft className="h-4 w-4" strokeWidth={2} />, label: "Collapse panel" },
  { icon: <Share2 className="h-4 w-4" strokeWidth={2} />, label: "Share dispatch" },
  { icon: <Upload className="h-4 w-4" strokeWidth={2} />, label: "Export report" },
  { icon: <Star className="h-4 w-4" strokeWidth={2} />, label: "Saved routes" },
  { icon: <Plus className="h-4 w-4" strokeWidth={2} />, label: "New shipment" },
  { icon: <Smartphone className="h-4 w-4" strokeWidth={2} />, label: "Driver app" },
  { icon: <Database className="h-4 w-4" strokeWidth={2} />, label: "Records" },
  { icon: <Calendar className="h-4 w-4" strokeWidth={2} />, label: "Schedule" },
  { icon: <Send className="h-4 w-4" strokeWidth={2} />, label: "Broadcast" },
  { icon: <AlertTriangle className="h-4 w-4" strokeWidth={2} />, label: "Active alerts", badge: true },
];

export function LeftRail() {
  // Local toggle — the dark theme isn't wired into globals yet, this is
  // a placeholder so the visual state matches the reference (sun = active
  // dark pill at the bottom, moon = inactive light circle above it).
  const [theme, setTheme] = useState<"light" | "dark">("light");

  return (
    <motion.aside
      initial={{ opacity: 0, x: -12 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.55, delay: 0.15, ease: [0.22, 1, 0.36, 1] }}
      className="hidden md:block fixed left-4 inset-y-0 z-30 w-12 pointer-events-none"
      aria-label="Quick actions"
    >
      {/* Action stack — vertically centered in the viewport */}
      <ul className="pointer-events-auto absolute left-0 right-0 top-1/2 -translate-y-1/2 flex flex-col items-center gap-2.5">
        {actions.map((a, i) => (
          <motion.li
            key={a.label}
            initial={{ opacity: 0, y: -8, scale: 0.85 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            transition={{
              duration: 0.4,
              delay: 0.25 + i * 0.04,
              ease: [0.22, 1, 0.36, 1],
            }}
          >
            <RailButton label={a.label} badge={a.badge}>
              {a.icon}
            </RailButton>
          </motion.li>
        ))}
      </ul>

      {/* Bottom — theme toggle pair, pinned to viewport bottom */}
      <motion.div
        initial={{ opacity: 0, y: 12 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.7, ease: [0.22, 1, 0.36, 1] }}
        className="pointer-events-auto absolute bottom-6 left-0 right-0 flex flex-col items-center gap-2.5"
      >
        <button
          aria-label="Dark mode"
          aria-pressed={theme === "dark"}
          onClick={() => setTheme("dark")}
          className={cn(
            "grid h-10 w-10 place-items-center rounded-full transition-all duration-200 cursor-pointer",
            theme === "dark"
              ? "bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_8px_20px_-6px_rgba(14,21,48,0.55)]"
              : "bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] hover:-translate-y-px hover:bg-white",
          )}
        >
          <Moon className="h-4 w-4" strokeWidth={2} />
        </button>
        <button
          aria-label="Light mode"
          aria-pressed={theme === "light"}
          onClick={() => setTheme("light")}
          className={cn(
            "grid h-10 w-10 place-items-center rounded-full transition-all duration-200 cursor-pointer",
            theme === "light"
              ? "bg-[var(--color-brand-900)] text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15),0_8px_20px_-6px_rgba(14,21,48,0.55)]"
              : "bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] hover:-translate-y-px hover:bg-white",
          )}
        >
          <Sun className="h-4 w-4" strokeWidth={2.2} />
        </button>
      </motion.div>
    </motion.aside>
  );
}

function RailButton({
  children,
  label,
  badge,
}: {
  children: React.ReactNode;
  label: string;
  badge?: boolean;
}) {
  return (
    <button
      aria-label={label}
      className="group relative grid h-10 w-10 place-items-center rounded-full bg-white/80 text-[var(--color-ink-700)] border border-[var(--color-ink-100)]/70 shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(15,23,42,0.08)] transition-all duration-200 hover:-translate-y-px hover:bg-white hover:text-[var(--color-ink-900)] hover:shadow-[inset_0_1px_0_rgba(255,255,255,0.95),0_6px_14px_-4px_rgba(15,23,42,0.14)] cursor-pointer"
    >
      {children}
      {badge && (
        <span className="absolute top-1 right-1 inline-flex">
          <StatusPulse tone="coral" />
        </span>
      )}

      {/* Tooltip on hover */}
      <span
        role="tooltip"
        className="pointer-events-none absolute left-full ml-3 whitespace-nowrap rounded-md bg-[var(--color-brand-900)] px-2 py-1 text-[11px] font-medium text-white opacity-0 translate-x-[-4px] transition-all duration-200 group-hover:opacity-100 group-hover:translate-x-0"
      >
        {label}
      </span>
    </button>
  );
}
