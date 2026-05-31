"use client";

import { Bell, Command, Search } from "lucide-react";
import { motion } from "motion/react";
import { Avatar } from "@/components/primitives/avatar";
import { StatusPulse } from "@/components/primitives/status-pulse";
import { cn } from "@/lib/utils";

const navItems = [
  { label: "Overview", active: true },
  { label: "Dispatch", active: false },
  { label: "Fleet", active: false },
  { label: "Routes", active: false },
  { label: "Analytics", active: false },
];

export function TopNav() {
  return (
    <motion.header
      initial={{ opacity: 0, y: -16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
      className="fixed top-4 inset-x-4 z-40 mx-auto max-w-[1340px]"
    >
      <div className="glass-pill flex items-center gap-2 rounded-full pl-2 pr-2 py-2 text-white">
        {/* Wordmark */}
        <a
          href="#"
          className="flex items-center gap-2 rounded-full pl-3 pr-4 py-2 transition-colors hover:bg-white/5"
        >
          <span className="relative grid h-9 w-9 place-items-center rounded-full bg-gradient-to-br from-[#6F7BFF] to-[#3441C8] shadow-[inset_0_1px_0_rgba(255,255,255,0.4),0_4px_10px_rgba(79,93,255,0.4)]">
            <span className="absolute inset-1 rounded-full border border-white/30" />
            <span className="absolute h-1.5 w-1.5 rounded-full bg-white" style={{ top: 6, right: 6 }} />
          </span>
          <span className="font-display text-[1.1rem] font-semibold tracking-tight">TMS</span>
        </a>

        {/* Nav items */}
        <nav className="hidden md:flex items-center gap-1 pl-2">
          {navItems.map((item) => (
            <a
              key={item.label}
              href="#"
              className={cn(
                "rounded-full px-4 py-2 text-[13px] font-medium transition-all duration-200",
                item.active
                  ? "bg-white text-[var(--color-brand-900)] shadow-[inset_0_1px_0_rgba(255,255,255,0.9),0_4px_12px_rgba(0,0,0,0.18)]"
                  : "text-white/70 hover:text-white hover:bg-white/8",
              )}
            >
              {item.label}
            </a>
          ))}
        </nav>

        <div className="flex-1" />

        {/* Search */}
        <div className="hidden lg:flex items-center gap-2 rounded-full bg-white/8 pl-3 pr-1.5 py-1.5 w-[280px] transition-colors hover:bg-white/10">
          <Search className="h-4 w-4 text-white/60" strokeWidth={2.2} />
          <input
            type="text"
            placeholder="Search shipments, drivers, routes…"
            className="flex-1 bg-transparent text-[13px] text-white placeholder:text-white/40 outline-none"
          />
          <kbd className="font-mono text-[10px] text-white/50 rounded-md bg-white/8 border border-white/10 px-1.5 py-0.5 flex items-center gap-0.5">
            <Command className="h-2.5 w-2.5" />K
          </kbd>
        </div>

        {/* Notifications */}
        <button
          aria-label="Notifications"
          className="relative grid h-10 w-10 place-items-center rounded-full text-white/80 transition-colors hover:bg-white/8 hover:text-white cursor-pointer"
        >
          <Bell className="h-4.5 w-4.5" strokeWidth={2} />
          <span className="absolute top-2 right-2 inline-flex">
            <StatusPulse tone="coral" />
          </span>
        </button>

        {/* User */}
        <button
          aria-label="Account"
          className="flex items-center gap-2 rounded-full bg-white/8 pl-1 pr-3 py-1 transition-colors hover:bg-white/12 cursor-pointer"
        >
          <Avatar name="Tuinui K." hue={32} size="sm" ring />
          <span className="hidden xl:inline text-[12.5px] font-medium text-white/90">Tuinui</span>
        </button>
      </div>
    </motion.header>
  );
}
