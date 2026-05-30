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
    <div className="flex min-h-screen flex-col">
      {/* Translucent sticky header. The hairline border is the only
          chrome — Apple keeps top bars almost invisible so content
          gets the visual budget. */}
      <header className="sticky top-0 z-40 border-b border-black/[0.06] bg-white/72 backdrop-blur-xl backdrop-saturate-180 dark:border-white/10 dark:bg-[#1C1C1E]/72">
        <div className="mx-auto flex max-w-[1600px] items-center justify-between gap-4 px-6 py-4 md:px-8 md:py-5">
          <div className="flex items-center gap-3">
            {/* Flat color chip — solid iOS blue, no gradient. */}
            <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary text-primary-foreground shadow-sm shadow-primary/20">
              <Sparkles className="h-4 w-4" strokeWidth={2.25} />
            </div>
            <div>
              <h1 className="text-[17px] font-semibold tracking-tight leading-tight">
                DTMS Templates
              </h1>
              <p className="text-[13px] text-muted-foreground">
                Compose RIOT3 action recipes and order plans.
              </p>
            </div>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setSelectedId(null)}
            disabled={selectedId === null}
            className="rounded-full px-4 text-[13px] font-medium text-primary hover:bg-primary/10 disabled:opacity-40"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
            New OrderTemplate
          </Button>
        </div>
      </header>

      {/* Tablet ≤1024px: stack panels vertically.
          Desktop ≥1024px: split panel, sized cards. */}
      <main className="mx-auto grid w-full max-w-[1600px] flex-1 grid-cols-1 gap-6 p-6 md:p-8 lg:grid-cols-[minmax(380px,28rem)_1fr] lg:gap-8">
        <aside className="glass flex min-h-0 flex-col overflow-hidden rounded-[24px] lg:max-h-[calc(100vh-7rem)]">
          <ActionTemplateList />
        </aside>

        <section className="glass flex min-h-0 flex-col overflow-hidden rounded-[24px] lg:max-h-[calc(100vh-7rem)]">
          <div className="flex items-center justify-between border-b border-black/[0.06] px-6 py-5 dark:border-white/10">
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

          <div className="flex-1 space-y-6 overflow-auto p-6">
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
