import { MapsExperience } from "@/components/facility/maps-experience";

export const metadata = {
  title: "Site cartography · TMS",
  description: "Live station topology synced from RIOT3.",
};

export default function FacilityMapsPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <MapsExperience />
      </main>
    </>
  );
}
