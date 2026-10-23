import { TripsExperience } from "@/components/dispatch/trips-experience";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

export const metadata = {
  title: "Trips · TMS",
  description: "Envelope-dispatched trips across every delivery order.",
};

export default function DispatchTripsPage() {
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <TripsExperience />
      </main>
    </>
  );
}
