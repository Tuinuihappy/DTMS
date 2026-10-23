import { ActionTemplatesExperience } from "@/components/action-templates/templates-experience";

export const metadata = {
  title: "Action templates · TMS",
  description: "Workflow building blocks for delivery orchestration.",
};

export default function ActionTemplatesPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <ActionTemplatesExperience />
      </main>
    </>
  );
}
