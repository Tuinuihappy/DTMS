"use client";

import { Activity, Bell, ShieldCheck, UserCircle2 } from "lucide-react";
import { AnimatePresence, LayoutGroup, motion } from "motion/react";
import { useState } from "react";
import { cn } from "@/lib/utils";
import { TabActivity } from "./tab-activity";
import { TabOverview } from "./tab-overview";
import { TabPreferences } from "./tab-preferences";
import { TabSecurity } from "./tab-security";

const EASE: [number, number, number, number] = [0.22, 1, 0.36, 1];

type TabKey = "overview" | "activity" | "security" | "preferences";

const tabs: { key: TabKey; label: string; icon: React.ReactNode; badge?: number }[] = [
  { key: "overview", label: "Overview", icon: <UserCircle2 className="h-3.5 w-3.5" strokeWidth={2.2} /> },
  { key: "activity", label: "Activity", icon: <Activity className="h-3.5 w-3.5" strokeWidth={2.2} />, badge: 3 },
  { key: "security", label: "Security", icon: <ShieldCheck className="h-3.5 w-3.5" strokeWidth={2.2} /> },
  { key: "preferences", label: "Preferences", icon: <Bell className="h-3.5 w-3.5" strokeWidth={2.2} /> },
];

export function ProfileTabs() {
  const [active, setActive] = useState<TabKey>("overview");

  return (
    <motion.section
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.6, delay: 1.05, ease: EASE }}
      className="mt-10"
    >
      {/* Tab strip */}
      <div className="relative">
        <div
          className="overflow-x-auto -mx-1 px-1 pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
          role="tablist"
          aria-label="Profile sections"
        >
          <LayoutGroup id="profile-tabs">
            <div className="flex items-center gap-1 min-w-max">
              {tabs.map((t) => {
                const isActive = active === t.key;
                return (
                  <button
                    key={t.key}
                    role="tab"
                    aria-selected={isActive}
                    aria-controls={`panel-${t.key}`}
                    id={`tab-${t.key}`}
                    onClick={() => setActive(t.key)}
                    className={cn(
                      "relative inline-flex items-center gap-2 rounded-full px-4 py-2.5 text-[13px] transition-colors cursor-pointer",
                      isActive
                        ? "font-semibold text-[var(--color-ink-900)]"
                        : "font-medium text-[var(--color-ink-500)] hover:text-[var(--color-ink-800)]",
                    )}
                  >
                    {isActive && (
                      <motion.span
                        layoutId="profile-tab-pill"
                        transition={{ type: "spring", stiffness: 380, damping: 32 }}
                        className="absolute inset-0 rounded-full bg-white shadow-[inset_0_1px_0_rgba(255,255,255,1),0_4px_14px_-6px_rgba(15,23,42,0.12)] dark:bg-white/[0.08] dark:shadow-[inset_0_1px_0_rgba(255,255,255,0.1)]"
                      />
                    )}
                    <span className="relative inline-flex items-center gap-1.5">
                      {t.icon}
                      {t.label}
                      {t.badge && (
                        <span
                          className={cn(
                            "ml-0.5 grid h-4 min-w-4 place-items-center rounded-full px-1 text-[10px] font-bold",
                            isActive
                              ? "bg-[var(--color-brand-900)] text-white dark:bg-white dark:text-[var(--color-brand-900)]"
                              : "bg-[var(--color-ink-100)] text-[var(--color-ink-600)] dark:bg-white/[0.08] dark:text-[var(--color-ink-600)]",
                          )}
                        >
                          {t.badge}
                        </span>
                      )}
                    </span>
                  </button>
                );
              })}
            </div>
          </LayoutGroup>
        </div>
        <div className="mt-1 h-px bg-[var(--color-ink-100)] dark:bg-white/[0.06]" />
      </div>

      {/* Panel */}
      <div className="mt-6 min-h-[420px]">
        <AnimatePresence mode="wait">
          <motion.div
            key={active}
            id={`panel-${active}`}
            role="tabpanel"
            aria-labelledby={`tab-${active}`}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }}
            transition={{ duration: 0.32, ease: EASE }}
          >
            {active === "overview" && <TabOverview />}
            {active === "activity" && <TabActivity />}
            {active === "security" && <TabSecurity />}
            {active === "preferences" && <TabPreferences />}
          </motion.div>
        </AnimatePresence>
      </div>
    </motion.section>
  );
}
