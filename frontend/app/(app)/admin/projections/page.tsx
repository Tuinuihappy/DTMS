import { AdminProjectionsExperience } from "@/components/admin/projections-experience";

export const metadata = {
  title: "Projection health · TMS",
  description:
    "Per-projector inbox stats + lag across DeliveryOrder / Planning / Dispatch / Fleet modules.",
};

export default function AdminProjectionsPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <AdminProjectionsExperience />
      </main>
    </>
  );
}
