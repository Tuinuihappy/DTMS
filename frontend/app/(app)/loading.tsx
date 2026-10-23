import { TableSkeleton } from "@/components/primitives/data-table/table-skeleton";

// Shown inside the persistent shell while a page's server component
// streams in, so a click answers immediately instead of freezing on the
// old page. Same <main> frame as the pages, so nothing shifts on arrival.
export default function AppLoading() {
  return (
    <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6">
      <div className="mb-5 h-10 w-56 skeleton-pulse rounded-xl bg-[var(--color-ink-100)] dark:bg-white/[0.05]" aria-hidden />
      <TableSkeleton />
    </main>
  );
}
