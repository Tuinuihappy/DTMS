"use client";

import { useAuth } from "@/components/auth/auth-provider";
import { FleetPulseStrip } from "@/components/home/fleet-pulse-strip";
import { HomeHero } from "@/components/home/home-hero";
import { QuickActions } from "@/components/home/quick-actions";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

/**
 * /home — the operator's premium home page. The live facility cartography
 * is the centerpiece of the hero; KPI tiles and quick-action cards spread
 * the rest of the page. Sits behind the LeftRail's Home button and the
 * Facility > Map child link.
 */
export default function HomePage() {
  const { user } = useAuth();
  const firstName = user?.displayName?.split(" ")[0] || "there";

  return (
    <>
      <TopNav />
      <LeftRail />
      <main
        className="layer-content mx-auto max-w-[1340px] px-4 pb-24 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out"
      >
        <HomeHero firstName={firstName} />
        <FleetPulseStrip />
        <QuickActions />

        <footer className="mt-20 flex flex-wrap items-center justify-between gap-3 text-[11.5px] text-[var(--color-ink-400)]">
          <div className="flex items-center gap-2">
            <span className="inline-block h-1 w-1 rounded-full bg-[var(--color-success)]" />
            <span>
              Realtime sync · streaming{" "}
              <span className="font-mono text-[var(--color-ink-600)]">RIOT3</span>
            </span>
          </div>
          <div>
            TMS · Operations Control v2.4
          </div>
        </footer>
      </main>
    </>
  );
}
