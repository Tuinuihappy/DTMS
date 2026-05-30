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

  // Pull the full list once and pluck out the selected one. The form
  // resets itself when the `template` prop changes (see useEffect there).
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
      {/* Sticky translucent header — floats over the aurora background.
          We keep it inside the flex column so it scrolls into view on
          short viewports but stays put on tall ones. */}
      <header className="sticky top-0 z-40 border-b border-white/40 bg-white/55 backdrop-blur-xl backdrop-saturate-150 dark:border-white/10 dark:bg-white/[0.04]">
        <div className="mx-auto flex max-w-[1600px] items-center justify-between gap-4 px-6 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-gradient-to-br from-indigo-500/90 via-violet-500/90 to-fuchsia-500/90 text-white shadow-lg shadow-indigo-500/20">
              <Sparkles className="h-4 w-4" />
            </div>
            <div>
              <h1 className="text-base font-semibold tracking-tight">
                DTMS Templates
              </h1>
              <p className="text-xs text-muted-foreground">
                Compose RIOT3 action recipes and order plans.
              </p>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setSelectedId(null)}
            disabled={selectedId === null}
            className="rounded-full border-white/40 bg-white/60 backdrop-blur-sm hover:bg-white/80 dark:border-white/10 dark:bg-white/5 dark:hover:bg-white/10"
          >
            <Plus className="h-3.5 w-3.5" />
            New OrderTemplate
          </Button>
        </div>
      </header>

      <main className="mx-auto grid w-full max-w-[1600px] flex-1 grid-cols-1 gap-6 p-6 lg:grid-cols-[minmax(360px,28rem)_1fr]">
        {/* Left pane — ActionTemplate catalog. Glass card with its own
            scroll. min-h-0 lets the inner flex child's overflow work
            even when this grid cell wants to grow with content. */}
        <aside className="glass flex min-h-0 flex-col overflow-hidden rounded-3xl lg:max-h-[calc(100vh-7rem)]">
          <ActionTemplateList />
        </aside>

        {/* Right pane — saved templates strip + composer form. The
            composer scrolls inside the card; the saved list stays
            pinned to the top. */}
        <section className="glass flex min-h-0 flex-col overflow-hidden rounded-3xl lg:max-h-[calc(100vh-7rem)]">
          <div className="flex items-center justify-between border-b border-white/40 px-5 py-4 dark:border-white/10">
            <div className="min-w-0">
              <h2 className="text-sm font-semibold tracking-tight">
                OrderTemplate composer
              </h2>
              <p className="mt-0.5 text-xs text-muted-foreground">
                {selected
                  ? `Editing ${selected.name}`
                  : "Building a new template"}
              </p>
            </div>
          </div>

          <div className="flex-1 space-y-6 overflow-auto p-5">
            <OrderTemplateList
              selectedId={selectedId}
              onSelect={setSelectedId}
              onInstantiate={setInstantiating}
              includeInactive={includeInactive}
              onIncludeInactiveChange={setIncludeInactive}
            />

            <Separator className="bg-white/40 dark:bg-white/10" />

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
