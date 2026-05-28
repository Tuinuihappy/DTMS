"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Plus } from "lucide-react";

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
    () =>
      orderQuery.data?.find((t) => t.id === selectedId) ?? null,
    [orderQuery.data, selectedId]
  );

  return (
    <div className="flex h-screen flex-col">
      <header className="flex items-center justify-between border-b px-6 py-3">
        <div>
          <h1 className="text-base font-semibold leading-none">
            DTMS Templates
          </h1>
          <p className="mt-1 text-xs text-muted-foreground">
            Compose RIOT3 action recipes and order plans.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setSelectedId(null)}
          disabled={selectedId === null}
        >
          <Plus className="h-3.5 w-3.5" />
          New OrderTemplate
        </Button>
      </header>

      <main className="grid flex-1 grid-cols-1 overflow-hidden lg:grid-cols-[minmax(340px,28rem)_1fr]">
        <aside className="hidden border-r lg:flex lg:flex-col">
          <ActionTemplateList />
        </aside>
        <section className="flex flex-col overflow-hidden">
          <div className="flex items-center justify-between border-b px-4 py-3">
            <div className="min-w-0">
              <h2 className="text-sm font-semibold leading-none">
                OrderTemplate composer
              </h2>
              <p className="mt-1 text-xs text-muted-foreground">
                {selected
                  ? `Editing ${selected.name}`
                  : "Building a new template"}
              </p>
            </div>
          </div>

          <div className="flex-1 space-y-5 overflow-auto p-4">
            <OrderTemplateList
              selectedId={selectedId}
              onSelect={setSelectedId}
              onInstantiate={setInstantiating}
              includeInactive={includeInactive}
              onIncludeInactiveChange={setIncludeInactive}
            />

            <Separator />

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
