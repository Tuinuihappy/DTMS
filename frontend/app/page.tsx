"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Inbox, Loader2, Plus } from "lucide-react";

import { actionTemplatesApi } from "@/lib/action-templates";
import { orderTemplatesApi } from "@/lib/order-templates";
import { queryKeys } from "@/lib/query-keys";
import type { ActionTemplateDto } from "@/types/action-template";
import type { OrderTemplateDto } from "@/types/order-template";

import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { ActionTemplateCard } from "@/components/action-template/action-template-card";
import { ActionTemplateForm } from "@/components/action-template/action-template-form";
import { OrderTemplateCard } from "@/components/order-template/order-template-card";
import { OrderTemplateForm } from "@/components/order-template/order-template-form";
import { InstantiateDialog } from "@/components/order-template/instantiate-dialog";
import { EmptyState } from "@/components/shared/empty-state";
import { HeroBanner } from "@/components/shell/hero-banner";
import { Sidebar, type TemplateFilter } from "@/components/shell/sidebar";
import { TopBar } from "@/components/shell/top-bar";

export default function Home() {
  // Sidebar filter + search drive what shows in the grids. Selected
  // OrderTemplate (when not null) reveals the inline editor at the
  // bottom of the page.
  const [filter, setFilter] = useState<TemplateFilter>("all");
  const [search, setSearch] = useState("");
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [instantiating, setInstantiating] = useState<OrderTemplateDto | null>(
    null
  );

  // ActionTemplate form (create / edit dialog) state.
  const [actionFormOpen, setActionFormOpen] = useState(false);
  const [actionFormEditing, setActionFormEditing] =
    useState<ActionTemplateDto | null>(null);

  // For the filter view we always want both active and inactive in the
  // dataset; we slice locally based on `filter`. Reduces query count.
  const actionQuery = useQuery({
    queryKey: queryKeys.actionTemplates.list({ includeInactive: true }),
    queryFn: () => actionTemplatesApi.list({ includeInactive: true }),
  });
  const orderQuery = useQuery({
    queryKey: queryKeys.orderTemplates.list({ includeInactive: true }),
    queryFn: () => orderTemplatesApi.list({ includeInactive: true }),
  });

  const actionAll = actionQuery.data ?? [];
  const orderAll = orderQuery.data ?? [];

  const visibleActions = useMemo(
    () => filterAndSearchActions(actionAll, filter, search),
    [actionAll, filter, search]
  );
  const visibleOrders = useMemo(
    () => filterAndSearchOrders(orderAll, filter, search),
    [orderAll, filter, search]
  );

  const showActions = filter !== "orders";
  const showOrders = filter !== "actions";

  const selectedOrder = useMemo(
    () => orderAll.find((t) => t.id === selectedOrderId) ?? null,
    [orderAll, selectedOrderId]
  );
  const latestOrder = useMemo(() => {
    if (orderAll.length === 0) return null;
    // The list comes back sorted server-side; first active item is fine
    // as "latest" until we wire a real `lastRunAt` field.
    return orderAll.find((t) => t.isActive) ?? orderAll[0];
  }, [orderAll]);

  const counts = useMemo(
    () => ({
      all: actionAll.length + orderAll.length,
      active:
        actionAll.filter((t) => t.isActive).length +
        orderAll.filter((t) => t.isActive).length,
      inactive:
        actionAll.filter((t) => !t.isActive).length +
        orderAll.filter((t) => !t.isActive).length,
      actions: actionAll.length,
      orders: orderAll.length,
    }),
    [actionAll, orderAll]
  );

  function openCreateAction() {
    setActionFormEditing(null);
    setActionFormOpen(true);
  }

  function openEditAction(t: ActionTemplateDto) {
    setActionFormEditing(t);
    setActionFormOpen(true);
  }

  return (
    <div className="grid min-h-screen grid-cols-1 gap-4 p-4 lg:grid-cols-[240px_1fr] lg:gap-5 lg:p-5">
      {/* ─── Left sidebar (collapses below lg) ────────────────────────── */}
      <div className="lg:sticky lg:top-5 lg:h-[calc(100vh-2.5rem)]">
        <Sidebar selected={filter} onSelect={setFilter} counts={counts} />
      </div>

      {/* ─── Main shell ───────────────────────────────────────────────── */}
      <div className="flex min-w-0 flex-col gap-5">
        <TopBar search={search} onSearchChange={setSearch} />

        <HeroBanner
          latest={latestOrder}
          totalAction={actionAll.length}
          totalOrder={orderAll.length}
          onInstantiate={setInstantiating}
          onCreateNewOrder={() => setSelectedOrderId(null)}
        />

        {actionQuery.isLoading || orderQuery.isLoading ? (
          <div className="liquid-glass flex items-center justify-center rounded-[24px] p-16">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
          </div>
        ) : (
          <>
            {showActions ? (
              <Section
                title="ActionTemplates"
                subtitle="Reusable RIOT3 ACT recipes."
                action={
                  <Button
                    size="sm"
                    onClick={openCreateAction}
                    className="liquid-pill-primary rounded-full px-4 font-medium"
                  >
                    <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
                    New
                  </Button>
                }
              >
                {visibleActions.length === 0 ? (
                  <EmptyState
                    icon={<Inbox className="h-7 w-7" />}
                    title={search ? "No matches" : "No ActionTemplates yet"}
                    description={
                      search
                        ? "Try a different search."
                        : "Create one to get started."
                    }
                  />
                ) : (
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                    {visibleActions.map((t) => (
                      <ActionTemplateCard
                        key={t.id}
                        template={t}
                        onEdit={openEditAction}
                      />
                    ))}
                  </div>
                )}
              </Section>
            ) : null}

            {showOrders ? (
              <Section
                title="OrderTemplates"
                subtitle="Composed RIOT3 order plans."
                action={
                  <Button
                    size="sm"
                    onClick={() => setSelectedOrderId(null)}
                    disabled={selectedOrderId === null}
                    variant="ghost"
                    className="press-feedback rounded-full px-4 text-[13px] font-medium text-primary hover:bg-primary/10 disabled:opacity-40"
                  >
                    <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
                    New
                  </Button>
                }
              >
                {visibleOrders.length === 0 ? (
                  <EmptyState
                    icon={<Inbox className="h-7 w-7" />}
                    title={search ? "No matches" : "No OrderTemplates yet"}
                    description={
                      search
                        ? "Try a different search."
                        : "Build the first one in the editor below."
                    }
                  />
                ) : (
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                    {visibleOrders.map((t) => (
                      <OrderTemplateCard
                        key={t.id}
                        template={t}
                        selected={t.id === selectedOrderId}
                        onSelect={setSelectedOrderId}
                        onInstantiate={setInstantiating}
                      />
                    ))}
                  </div>
                )}
              </Section>
            ) : null}

            {showOrders ? (
              <Section
                title={selectedOrder ? "Editor" : "Composer"}
                subtitle={
                  selectedOrder
                    ? `Editing ${selectedOrder.name}`
                    : "Building a new OrderTemplate"
                }
              >
                <div className="liquid-glass relative rounded-[24px] p-6">
                  <div className="relative z-[2]">
                    <OrderTemplateForm
                      template={selectedOrder}
                      onSaved={(id) => setSelectedOrderId(id)}
                      onCancel={
                        selectedOrder ? () => setSelectedOrderId(null) : undefined
                      }
                    />
                  </div>
                </div>
              </Section>
            ) : null}
          </>
        )}
      </div>

      <ActionTemplateForm
        open={actionFormOpen}
        onOpenChange={setActionFormOpen}
        template={actionFormEditing}
      />

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

// ── helpers ──────────────────────────────────────────────────────────────

function Section({
  title,
  subtitle,
  action,
  children,
}: {
  title: string;
  subtitle?: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div className="flex items-end justify-between gap-3">
        <div>
          <h2 className="text-[15px] font-semibold tracking-tight leading-tight">
            {title}
          </h2>
          {subtitle ? (
            <p className="mt-0.5 text-[12px] text-muted-foreground">
              {subtitle}
            </p>
          ) : null}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function matchesSearch(
  text: string,
  search: string
): boolean {
  if (search.trim().length === 0) return true;
  return text.toLowerCase().includes(search.trim().toLowerCase());
}

function filterAndSearchActions(
  list: ActionTemplateDto[],
  filter: TemplateFilter,
  search: string
): ActionTemplateDto[] {
  return list.filter((t) => {
    if (filter === "orders") return false;
    if (filter === "active" && !t.isActive) return false;
    if (filter === "inactive" && t.isActive) return false;
    return matchesSearch(`${t.name} ${t.actionType}`, search);
  });
}

function filterAndSearchOrders(
  list: OrderTemplateDto[],
  filter: TemplateFilter,
  search: string
): OrderTemplateDto[] {
  return list.filter((t) => {
    if (filter === "actions") return false;
    if (filter === "active" && !t.isActive) return false;
    if (filter === "inactive" && t.isActive) return false;
    return matchesSearch(`${t.name} ${t.description ?? ""}`, search);
  });
}

// Separator kept in scope so the form imports don't tree-shake it.
// (Order template form uses it internally.)
void Separator;
