import { ReportsExperience } from "@/components/reports/reports-experience";

export const metadata = {
  title: "Reports · TMS",
  description:
    "Pre-built reports backed by the OrderFacts BI projection — drill, filter, export.",
};

export default function ReportsPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <ReportsExperience />
      </main>
    </>
  );
}
