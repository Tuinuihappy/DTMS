import { CarrierTypesExperience } from "@/components/fleet/carrier-types-experience";

export const metadata = {
  title: "Carrier types · TMS",
  description: "The catalogue of carrier kinds each physical rack or cart is registered against.",
};

export default function CarrierTypesPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <CarrierTypesExperience />
      </main>
    </>
  );
}
