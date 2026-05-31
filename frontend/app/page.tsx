import { CommsCard } from "@/components/dashboard/comms-card";
import { DispatchFunnel } from "@/components/dashboard/dispatch-funnel";
import { DriverLeaderboard } from "@/components/dashboard/driver-leaderboard";
import { KpiRail } from "@/components/dashboard/kpi-rail";
import { LiveActivityCard } from "@/components/dashboard/live-activity-card";
import { PriorityShipments } from "@/components/dashboard/priority-shipments";
import { GreetingStrip } from "@/components/shell/greeting-strip";
import { TopNav } from "@/components/shell/top-nav";

export default function DashboardPage() {
  return (
    <>
      <TopNav />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-24 pt-28 sm:px-6 md:pt-32">
        <GreetingStrip />

        <section className="grid grid-cols-12 gap-5">
          <LiveActivityCard />
          <KpiRail />
          <PriorityShipments />
          <DriverLeaderboard />
          <DispatchFunnel />
          <CommsCard />
        </section>

        <footer className="mt-16 flex flex-wrap items-center justify-between gap-3 text-[11.5px] text-[var(--color-ink-400)]">
          <div className="flex items-center gap-2">
            <span className="inline-block h-1 w-1 rounded-full bg-[var(--color-success)]" />
            <span>
              Realtime sync · last refresh{" "}
              <span className="font-mono text-[var(--color-ink-600)]">07:42:18</span>
            </span>
          </div>
          <div>
            TMS · Operations Control v2.4 · build{" "}
            <span className="font-mono text-[var(--color-ink-600)]">4aa1133</span>
          </div>
        </footer>
      </main>
    </>
  );
}
