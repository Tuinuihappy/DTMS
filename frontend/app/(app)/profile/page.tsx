import { ProfileProvider } from "@/components/profile/profile-context";
import { ProfileHero } from "@/components/profile/profile-hero";
import { ProfileStats } from "@/components/profile/profile-stats";
import { ProfileTabs } from "@/components/profile/profile-tabs";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

export default function ProfilePage() {
  return (
    <ProfileProvider>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-24 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <ProfileHero />
        <ProfileStats />
        <ProfileTabs />

        <footer className="mt-16 flex flex-wrap items-center justify-between gap-3 text-[11.5px] text-[var(--color-ink-400)]">
          <div className="flex items-center gap-2">
            <span className="inline-block h-1 w-1 rounded-full bg-[var(--color-success)]" />
            <span>Profile last synced 12 sec ago</span>
          </div>
          <div>
            TMS · Operations Control v2.4 · build{" "}
            <span className="font-mono text-[var(--color-ink-600)]">4aa1133</span>
          </div>
        </footer>
      </main>
    </ProfileProvider>
  );
}
