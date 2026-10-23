import { TemplateEditor } from "@/components/order-templates/template-editor";
import { LeftRail } from "@/components/shell/left-rail";
import { TopNav } from "@/components/shell/top-nav";

export const metadata = {
  title: "New order template · TMS",
  description: "Compose a reusable order recipe.",
};

export default function NewOrderTemplatePage() {
  return (
    <>
      <TopNav />
      <LeftRail />
      <main className="layer-content mx-auto max-w-[1340px] px-4 pb-32 pt-28 sm:px-6 md:px-6 md:pt-32 lg:pl-[var(--rail-width,80px)] lg:pr-6 transition-[padding] duration-300 ease-out">
        <TemplateEditor />
      </main>
    </>
  );
}
