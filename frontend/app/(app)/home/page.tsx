"use client";

import { HomeOverview } from "@/components/home/home-overview";

/**
 * /home — operator landing. Live facility cartography on the left, mocked
 * fleet KPIs on the right. Full MapsExperience (edit drawer, RIOT3 sync) is
 * still at /facility/maps; this page is the dashboard glance.
 */
export default function HomePage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-16 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <HomeOverview />
      </main>
    </>
  );
}
