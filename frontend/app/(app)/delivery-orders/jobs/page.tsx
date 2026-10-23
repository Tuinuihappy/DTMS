import { JobsExperience } from "@/components/planning/jobs-experience";

export const metadata = {
  title: "Planning jobs queue · TMS",
  description: "Operator queue of Planning Jobs across orders — Failed (retriable) + Stuck.",
};

export default function JobsQueuePage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <JobsExperience />
      </main>
    </>
  );
}
