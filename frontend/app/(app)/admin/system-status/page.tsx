import { AdminSystemStatusExperience } from "@/components/admin/system-status-experience";

export const metadata = {
  title: "System status · TMS",
  description:
    "Realtime health of external vendors (RIOT3, OMS) with state-machine-debounced transitions and SignalR push updates.",
};

export default function AdminSystemStatusPage() {
  return (
    <>
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
        <AdminSystemStatusExperience />
      </main>
    </>
  );
}
