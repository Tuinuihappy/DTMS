import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";
import { EditTemplateLoader } from "@/components/order-templates/edit-template-loader";

export const metadata = {
  title: "Edit order template · TMS",
};

type Ctx = { params: Promise<{ id: string }> };

export default async function EditOrderTemplatePage({ params }: Ctx) {
  const { id } = await params;
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <EditTemplateLoader id={id} />
      </main>
    </>
  );
}
