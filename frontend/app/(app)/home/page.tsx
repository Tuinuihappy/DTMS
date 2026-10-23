"use client";

import { HomeOverview } from "@/components/home/home-overview";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

/**
 * /home — operator landing. Live facility cartography on the left, mocked
 * fleet KPIs on the right. Full MapsExperience (edit drawer, RIOT3 sync) is
 * still at /facility/maps; this page is the dashboard glance.
 */
export default function HomePage() {
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-16 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <HomeOverview />
      </main>
    </>
  );
}
