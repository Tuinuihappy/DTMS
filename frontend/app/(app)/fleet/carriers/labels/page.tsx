import { CarrierLabelsExperience } from "@/components/fleet/carrier-labels-experience";

export const metadata = {
  title: "Carrier labels · TMS",
  description: "Printable QR labels for physical carriers.",
};

export default function FleetCarrierLabelsPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <CarrierLabelsExperience />
      </main>
    </>
  );
}
