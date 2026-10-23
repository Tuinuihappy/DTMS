import { RobotsAnalysisExperience } from "@/components/dashboard/robots-analysis-experience";

export const metadata = {
  title: "Robot analysis · TMS",
  description: "Hourly fleet utilization from the FleetUtilizationHourly projection.",
};

export default function RobotsAnalysisPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <RobotsAnalysisExperience />
      </main>
    </>
  );
}
