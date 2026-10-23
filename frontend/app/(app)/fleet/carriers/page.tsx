import { CarriersExperience } from "@/components/fleet/carriers-experience";

export const metadata = {
  title: "Carriers · TMS",
  description: "Registry of physical carriers — racks and carts.",
};

export default function FleetCarriersPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <CarriersExperience />
      </main>
    </>
  );
}
