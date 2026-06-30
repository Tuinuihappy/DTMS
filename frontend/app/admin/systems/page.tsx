import { IamSystemsExperience } from "@/components/admin/iam-systems-experience";
import { PermissionGuard } from "@/components/auth/permission-guard";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

export const metadata = {
  title: "Source systems · IAM · TMS",
  description: "Manage federated source systems (SystemClient + credentials).",
};

export default function AdminSystemsPage() {
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <PermissionGuard requires="dtms:iam:system:read">
          <IamSystemsExperience />
        </PermissionGuard>
      </main>
    </>
  );
}
