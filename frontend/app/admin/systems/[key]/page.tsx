import { IamSystemDetailExperience } from "@/components/admin/iam-system-detail-experience";
import { PermissionGuard } from "@/components/auth/permission-guard";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";
import { Permissions } from "@/lib/auth/permissions";

type Props = { params: Promise<{ key: string }> };

export async function generateMetadata({ params }: Props) {
  const { key } = await params;
  return {
    title: `${key} · Source systems · IAM · TMS`,
    description: `Manage SystemClient ${key}: metadata, credential, callback config.`,
  };
}

export default async function AdminSystemDetailPage({ params }: Props) {
  const { key } = await params;
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <PermissionGuard requires={Permissions.Iam.SystemRead}>
          <IamSystemDetailExperience systemKey={key} />
        </PermissionGuard>
      </main>
    </>
  );
}
