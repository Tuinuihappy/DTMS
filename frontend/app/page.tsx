"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Plus, Sparkles } from "lucide-react";

import { orderTemplatesApi } from "@/lib/order-templates";
import { queryKeys } from "@/lib/query-keys";
import type { OrderTemplateDto } from "@/types/order-template";

import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { ActionTemplateList } from "@/components/action-template/action-template-list";
import { OrderTemplateForm } from "@/components/order-template/order-template-form";
import { OrderTemplateList } from "@/components/order-template/order-template-list";
import { InstantiateDialog } from "@/components/order-template/instantiate-dialog";

export default function Home() {
  // Right pane mode: null = the composer is in "new template" mode;
  // a string = editing the picked saved template by id.
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [includeInactive, setIncludeInactive] = useState(false);
  const [instantiating, setInstantiating] = useState<OrderTemplateDto | null>(
    null
  );

  const orderQuery = useQuery({
    queryKey: queryKeys.orderTemplates.list({ includeInactive }),
    queryFn: () => orderTemplatesApi.list({ includeInactive }),
  });

  const selected = useMemo(
    () => orderQuery.data?.find((t) => t.id === selectedId) ?? null,
    [orderQuery.data, selectedId]
  );

  return (
    <div className="relative flex min-h-screen flex-col">
      {/* iOS 26 Floating Pill Header — detaches from the page edges, hovers
          over content. The page below scrolls under it. Width is capped so
          the pill never reaches the viewport edges (Apple keeps ~16px gap
          even on iPad). */}
      <header className="sticky top-0 z-40 flex justify-center px-4 pt-4 md:px-6 md:pt-6">
        <div className="liquid-glass mx-auto flex w-full max-w-[1560px] items-center justify-between gap-4 rounded-full px-3 py-2 md:px-4 md:py-2.5">
          <div className="relative z-[2] flex items-center gap-3 pl-1">
            {/* Solid iOS-blue chip — the only saturated color on the surface. */}
            <div className="flex h-9 w-9 items-center justify-center rounded-2xl bg-primary text-primary-foreground shadow-sm shadow-primary/25">
              <Sparkles className="h-4 w-4" strokeWidth={2.25} />
            </div>
            <div className="hidden sm:block">
              <h1 className="text-[15px] font-semibold tracking-tight leading-tight">
                DTMS Templates
              </h1>
              <p className="text-[12px] text-muted-foreground leading-tight">
                Compose RIOT3 action recipes and order plans.
              </p>
            </div>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setSelectedId(null)}
            disabled={selectedId === null}
            className="press-feedback relative z-[2] rounded-full px-4 text-[13px] font-medium text-primary hover:bg-primary/10 disabled:opacity-40"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
            New OrderTemplate
          </Button>
        </div>
      </header>

      {/* Content edges to viewport. Top padding pushes below the floating
          header pill (visual height ~64px + spacing). Tablet ≤1024px stacks
          panels vertically per the iPad-app brief. */}
      <main className="mx-auto grid w-full max-w-[1600px] flex-1 grid-cols-1 gap-5 px-4 pb-6 pt-4 md:px-6 md:gap-6 md:pb-8 lg:grid-cols-[minmax(380px,28rem)_1fr]">
        <aside className="liquid-glass flex min-h-0 flex-col overflow-hidden rounded-[28px] lg:max-h-[calc(100vh-8rem)]">
          <ActionTemplateList />
        </aside>

        <section className="liquid-glass flex min-h-0 flex-col overflow-hidden rounded-[28px] lg:max-h-[calc(100vh-8rem)]">
          <div className="relative z-[2] flex items-center justify-between border-b border-black/[0.06] px-6 py-5 dark:border-white/10">
            <div className="min-w-0">
              <h2 className="text-[15px] font-semibold tracking-tight leading-tight">
                OrderTemplate composer
              </h2>
              <p className="mt-1 text-[13px] text-muted-foreground">
                {selected
                  ? `Editing ${selected.name}`
                  : "Building a new template"}
              </p>
            </div>
          </div>

          <div className="relative z-[2] flex-1 space-y-6 overflow-auto p-6">
            <OrderTemplateList
              selectedId={selectedId}
              onSelect={setSelectedId}
              onInstantiate={setInstantiating}
              includeInactive={includeInactive}
              onIncludeInactiveChange={setIncludeInactive}
            />

            <Separator className="bg-black/[0.06] dark:bg-white/10" />

            <OrderTemplateForm
              template={selected}
              onSaved={(id) => setSelectedId(id)}
              onCancel={selected ? () => setSelectedId(null) : undefined}
            />
          </div>
        </section>
      </main>

      <InstantiateDialog
        open={instantiating !== null}
        onOpenChange={(o) => {
          if (!o) setInstantiating(null);
        }}
        template={instantiating}
      />
    </div>
  );
}
