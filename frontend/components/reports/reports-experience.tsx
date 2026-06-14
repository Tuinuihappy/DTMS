"use client";

import {
  Activity,
  AlertTriangle,
  BarChart3,
  Building2,
  Clock,
  Wrench,
} from "lucide-react";
import { useMemo, useState } from "react";
import { cn } from "@/lib/utils";
import { JobFailuresReport } from "./job-failures-report";
import { LeadTimeReport } from "./lead-time-report";
import { OrdersSummaryReport } from "./orders-summary-report";
import { SlaBreachReport } from "./sla-breach-report";
import { TopFailuresReport } from "./top-failures-report";
import { VehiclePerformanceReport } from "./vehicle-performance-report";
import { buildWindowRange, WindowToggle, type ReportWindow } from "./window-toggle";

type TabKey = "orders" | "sla" | "failures" | "job-failures" | "vehicles" | "lead-time";

const TABS: { key: TabKey; label: string; icon: React.ReactNode; hint: string }[] = [
  { key: "orders",        label: "Orders by priority",   icon: <BarChart3 className="h-3.5 w-3.5" />,    hint: "Pivot OrderFacts by Priority × FinalStatus" },
  { key: "sla",           label: "SLA breach",           icon: <AlertTriangle className="h-3.5 w-3.5" />, hint: "Confirm > 4h · Complete > 24h" },
  { key: "failures",      label: "Top failures",         icon: <Activity className="h-3.5 w-3.5" />,    hint: "Most common FailureReason text across terminal orders" },
  { key: "job-failures",  label: "Job failures",         icon: <Wrench className="h-3.5 w-3.5" />,      hint: "Structured JobFailureCategory breakdown from JobFacts (b13 enum)" },
  { key: "vehicles",      label: "Vehicle performance",  icon: <Building2 className="h-3.5 w-3.5" />,   hint: "Throughput + success rate per robot (VendorVehicleKey) from TripFacts" },
  { key: "lead-time",     label: "Lead time",            icon: <Clock className="h-3.5 w-3.5" />,       hint: "Histogram of TimeToComplete with p50/p95" },
];

export function ReportsExperience() {
  const [tab, setTab] = useState<TabKey>("orders");
  const [windowKey, setWindowKey] = useState<ReportWindow>("7d");
  const window = useMemo(() => buildWindowRange(windowKey), [windowKey]);
  const activeTab = TABS.find((t) => t.key === tab) ?? TABS[0];

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <h1 className="text-[1.7rem] font-semibold text-[var(--color-ink-900)]">
            Reports
          </h1>
          <p className="mt-1 text-[13px] text-[var(--color-ink-500)]">
            Pre-built reports backed by the OrderFacts / TripFacts projections.
            {" "}
            <span className="text-[var(--color-ink-400)]">— {activeTab.hint}</span>
          </p>
        </div>
        <WindowToggle value={windowKey} onChange={setWindowKey} />
      </header>

      <nav className="flex gap-2 overflow-x-auto pb-1 [&::-webkit-scrollbar]:h-1">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            onClick={() => setTab(t.key)}
            className={cn(
              "inline-flex shrink-0 items-center gap-1.5 rounded-full px-3.5 py-2 text-[12px] font-semibold transition-all",
              tab === t.key
                ? "bg-[var(--color-brand-900)] text-white shadow-[0_6px_16px_-8px_rgba(15,23,42,0.4)] dark:bg-[var(--color-brand-500)] dark:text-[var(--color-ink-50)]"
                : "bg-white/60 text-[var(--color-ink-600)] border border-white/70 hover:bg-white/90 dark:bg-white/[0.05] dark:border-white/10 dark:hover:bg-white/[0.1]",
            )}
          >
            {t.icon}
            {t.label}
          </button>
        ))}
      </nav>

      {tab === "orders" && <OrdersSummaryReport window={window} />}
      {tab === "sla" && <SlaBreachReport window={window} />}
      {tab === "failures" && <TopFailuresReport window={window} />}
      {tab === "job-failures" && <JobFailuresReport window={window} />}
      {tab === "vehicles" && <VehiclePerformanceReport window={window} />}
      {tab === "lead-time" && <LeadTimeReport window={window} />}
    </div>
  );
}
