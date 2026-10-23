import { ItemsExperience } from "@/components/items/items-experience";

export const metadata = {
  title: "Items · TMS",
  description: "Search delivery-order items across every order.",
};

export default function ItemsPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <ItemsExperience />
      </main>
    </>
  );
}
