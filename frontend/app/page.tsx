"use client";

import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Inbox, Loader2, PackageCheck, Plus } from "lucide-react";

import { actionTemplatesApi } from "@/lib/action-templates";
import { deliveryOrdersApi } from "@/lib/delivery-orders";
import { orderTemplatesApi } from "@/lib/order-templates";
import { queryKeys } from "@/lib/query-keys";
import type { ActionTemplateDto } from "@/types/action-template";
import type { OrderTemplateDto } from "@/types/order-template";
import {
  ORDER_STATUS_VALUES,
  formatEnumLabel,
  type DeliveryOrderListDto,
  type OrderStatus,
} from "@/types/delivery-order";

import { Button } from "@/components/ui/button";
import { ActionTemplateCard } from "@/components/action-template/action-template-card";
import { ActionTemplateForm } from "@/components/action-template/action-template-form";
import { OrderTemplateCard } from "@/components/order-template/order-template-card";
import { OrderTemplateForm } from "@/components/order-template/order-template-form";
import { InstantiateDialog } from "@/components/order-template/instantiate-dialog";
import { DeliveryOrderCard } from "@/components/delivery-order/delivery-order-card";
import { DeliveryOrderDetailSheet } from "@/components/delivery-order/delivery-order-detail-sheet";
import { DeliveryOrderFormSheet } from "@/components/delivery-order/delivery-order-form-sheet";
import { EmptyState } from "@/components/shared/empty-state";
import { HeroBanner } from "@/components/shell/hero-banner";
import { Sidebar, type NavFilter } from "@/components/shell/sidebar";
import { TopBar } from "@/components/shell/top-bar";
import { cn } from "@/lib/utils";

// In-view Active/Inactive toggle for the template grids. "all" = no
// status filter. Lives inside the page, not the sidebar — the rail
// stays focused on top-level navigation.
type ActiveFilter = "all" | "active" | "inactive";
const ACTIVE_FILTERS: { label: string; value: ActiveFilter }[] = [
  { label: "All", value: "all" },
  { label: "Active", value: "active" },
  { label: "Inactive", value: "inactive" },
];

// Pill strip filter values for the Delivery Orders view. "all" maps to
// "no status filter on the API request".
const STATUS_FILTERS: { label: string; value: "all" | OrderStatus }[] = [
  { label: "All", value: "all" },
  ...ORDER_STATUS_VALUES.map((s) => ({ label: formatEnumLabel(s), value: s })),
];

export default function Home() {
  const [filter, setFilter] = useState<NavFilter>("actions");
  const [search, setSearch] = useState("");
  const [selectedOrderId, setSelectedOrderId] = useState<string | null>(null);
  const [instantiating, setInstantiating] = useState<OrderTemplateDto | null>(
    null
  );

  // Per-view Active/Inactive filter, scoped to whichever template grid
  // is on screen. Kept separate so switching between actions/orders
  // doesn't leak state across the views.
  const [actionStatusFilter, setActionStatusFilter] =
    useState<ActiveFilter>("all");
  const [orderStatusFilter, setOrderStatusFilter] =
    useState<ActiveFilter>("all");

  const [actionFormOpen, setActionFormOpen] = useState(false);
  const [actionFormEditing, setActionFormEditing] =
    useState<ActionTemplateDto | null>(null);

  // Delivery Orders state.
  const [doStatusFilter, setDoStatusFilter] = useState<"all" | OrderStatus>(
    "all"
  );
  const [doFormSheetOpen, setDoFormSheetOpen] = useState(false);
  const [doDetailId, setDoDetailId] = useState<string | null>(null);

  // Template queries (used for the Templates view + sidebar counts).
  const actionQuery = useQuery({
    queryKey: queryKeys.actionTemplates.list({ includeInactive: true }),
    queryFn: () => actionTemplatesApi.list({ includeInactive: true }),
  });
  const orderQuery = useQuery({
    queryKey: queryKeys.orderTemplates.list({ includeInactive: true }),
    queryFn: () => orderTemplatesApi.list({ includeInactive: true }),
  });

  // Delivery Orders query — sidebar count uses an unfiltered fetch so
  // navigating into the DO view doesn't have to hit the network for the
  // total. The DO view re-uses this same query when the pill filter is
  // "all" and only re-fetches when a status is picked.
  const deliveryOrdersAllQuery = useQuery({
    queryKey: queryKeys.deliveryOrders.list(),
    queryFn: () => deliveryOrdersApi.list({ pageSize: 100 }),
  });
  const deliveryOrdersFilteredQuery = useQuery({
    queryKey: queryKeys.deliveryOrders.list({
      status: doStatusFilter === "all" ? undefined : doStatusFilter,
    }),
    queryFn: () =>
      deliveryOrdersApi.list({
        status: doStatusFilter === "all" ? undefined : doStatusFilter,
        pageSize: 100,
      }),
    enabled: filter === "delivery-orders",
  });

  const actionAll = actionQuery.data ?? [];
  const orderAll = orderQuery.data ?? [];
  const deliveryAll = deliveryOrdersAllQuery.data?.data ?? [];
  const deliveryVisible =
    doStatusFilter === "all"
      ? deliveryAll
      : deliveryOrdersFilteredQuery.data?.data ?? [];

  const visibleActions = useMemo(
    () => filterAndSearchActions(actionAll, actionStatusFilter, search),
    [actionAll, actionStatusFilter, search]
  );
  const visibleOrders = useMemo(
    () => filterAndSearchOrders(orderAll, orderStatusFilter, search),
    [orderAll, orderStatusFilter, search]
  );
  const visibleDeliveryOrders = useMemo(
    () =>
      deliveryVisible.filter((o) =>
        matchesSearch(`${o.orderRef} ${o.priority}`, search)
      ),
    [deliveryVisible, search]
  );

  const selectedOrderTemplate = useMemo(
    () => orderAll.find((t) => t.id === selectedOrderId) ?? null,
    [orderAll, selectedOrderId]
  );
  const latestOrderTemplate = useMemo(() => {
    if (orderAll.length === 0) return null;
    return orderAll.find((t) => t.isActive) ?? orderAll[0];
  }, [orderAll]);

  const counts = useMemo(
    () => ({
      actions: actionAll.length,
      orders: orderAll.length,
      "delivery-orders":
        deliveryOrdersAllQuery.data?.totalCount ?? deliveryAll.length,
    }),
    [
      actionAll.length,
      orderAll.length,
      deliveryAll.length,
      deliveryOrdersAllQuery.data?.totalCount,
    ]
  );

  function openCreateAction() {
    setActionFormEditing(null);
    setActionFormOpen(true);
  }
  function openEditAction(t: ActionTemplateDto) {
    setActionFormEditing(t);
    setActionFormOpen(true);
  }
  function openDeliveryDetail(id: string) {
    setDoDetailId(id);
  }

  const isDeliveryView = filter === "delivery-orders";
  const isActionsView = filter === "actions";
  const isOrdersView = filter === "orders";

  return (
    <div className="grid min-h-screen grid-cols-1 gap-4 p-4 lg:grid-cols-[240px_1fr] lg:gap-5 lg:p-5">
      {/* ─── Left sidebar ────────────────────────────────────────────── */}
      <div className="lg:sticky lg:top-5 lg:h-[calc(100vh-2.5rem)]">
        <Sidebar selected={filter} onSelect={setFilter} counts={counts} />
      </div>

      {/* ─── Main shell ──────────────────────────────────────────────── */}
      <div className="flex min-w-0 flex-col gap-5">
        <TopBar search={search} onSearchChange={setSearch} />

        {isDeliveryView ? (
          <DeliveryOrdersView
            statusFilter={doStatusFilter}
            onStatusFilterChange={setDoStatusFilter}
            ordersByStatus={countByStatus(deliveryAll)}
            visible={visibleDeliveryOrders}
            isLoading={
              deliveryOrdersAllQuery.isLoading ||
              (doStatusFilter !== "all" && deliveryOrdersFilteredQuery.isLoading)
            }
            search={search}
            selectedId={doDetailId}
            onSelectCard={openDeliveryDetail}
            onCreate={() => setDoFormSheetOpen(true)}
          />
        ) : (
          <>
            <HeroBanner
              latest={latestOrderTemplate}
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
                {isActionsView ? (
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
                    <PillStrip
                      filters={ACTIVE_FILTERS}
                      value={actionStatusFilter}
                      onChange={setActionStatusFilter}
                      counts={countActive(actionAll)}
                    />
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

                {isOrdersView ? (
                  <>
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
                      <PillStrip
                        filters={ACTIVE_FILTERS}
                        value={orderStatusFilter}
                        onChange={setOrderStatusFilter}
                        counts={countActive(orderAll)}
                      />
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

                    <Section
                      title={selectedOrderTemplate ? "Editor" : "Composer"}
                      subtitle={
                        selectedOrderTemplate
                          ? `Editing ${selectedOrderTemplate.name}`
                          : "Building a new OrderTemplate"
                      }
                    >
                      <div className="liquid-glass relative rounded-[24px] p-6">
                        <div className="relative z-[2]">
                          <OrderTemplateForm
                            template={selectedOrderTemplate}
                            onSaved={(id) => setSelectedOrderId(id)}
                            onCancel={
                              selectedOrderTemplate
                                ? () => setSelectedOrderId(null)
                                : undefined
                            }
                          />
                        </div>
                      </div>
                    </Section>
                  </>
                ) : null}
              </>
            )}
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

      <DeliveryOrderFormSheet
        open={doFormSheetOpen}
        onOpenChange={setDoFormSheetOpen}
        onCreated={(id) => setDoDetailId(id)}
      />

      <DeliveryOrderDetailSheet
        open={doDetailId !== null}
        onOpenChange={(o) => {
          if (!o) setDoDetailId(null);
        }}
        orderId={doDetailId}
      />
    </div>
  );
}

// ── Delivery Orders view ─────────────────────────────────────────────────

interface DeliveryOrdersViewProps {
  statusFilter: "all" | OrderStatus;
  onStatusFilterChange: (next: "all" | OrderStatus) => void;
  ordersByStatus: Partial<Record<OrderStatus, number>>;
  visible: DeliveryOrderListDto[];
  isLoading: boolean;
  search: string;
  selectedId: string | null;
  onSelectCard: (id: string) => void;
  onCreate: () => void;
}

function DeliveryOrdersView(props: DeliveryOrdersViewProps) {
  const {
    statusFilter,
    onStatusFilterChange,
    ordersByStatus,
    visible,
    isLoading,
    search,
    selectedId,
    onSelectCard,
    onCreate,
  } = props;

  return (
    <>
      <section className="liquid-glass relative overflow-hidden rounded-[24px] p-6">
        <div className="relative z-[2] flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="liquid-puck flex h-10 w-10 items-center justify-center rounded-2xl">
              <PackageCheck
                className="relative z-[2] h-4 w-4 text-foreground"
                strokeWidth={2.25}
              />
            </div>
            <div>
              <h2 className="text-[17px] font-semibold tracking-tight leading-tight">
                Delivery Orders
              </h2>
              <p className="text-[12px] text-muted-foreground">
                Runtime orders that Planning will turn into RIOT3 jobs.
              </p>
            </div>
          </div>
          <Button
            size="sm"
            onClick={onCreate}
            className="liquid-pill-primary rounded-full px-4 font-medium"
          >
            <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
            New draft
          </Button>
        </div>
      </section>

      <Section title="Filter" subtitle="By lifecycle status.">
        <div className="liquid-glass-subtle relative flex flex-wrap items-center gap-1.5 rounded-2xl p-2">
          {STATUS_FILTERS.map((f) => {
            const count = f.value === "all" ? undefined : ordersByStatus[f.value];
            const active = statusFilter === f.value;
            return (
              <button
                key={f.value}
                type="button"
                onClick={() => onStatusFilterChange(f.value)}
                className={cn(
                  "press-feedback inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-[12px] font-medium transition-colors",
                  active
                    ? "bg-primary/15 text-primary"
                    : "text-muted-foreground hover:bg-black/[0.04] dark:hover:bg-white/[0.05]"
                )}
              >
                {f.label}
                {count !== undefined && count > 0 ? (
                  <span
                    className={cn(
                      "rounded-full px-1.5 text-[10px]",
                      active ? "bg-primary/20" : "bg-black/[0.05] dark:bg-white/[0.08]"
                    )}
                  >
                    {count}
                  </span>
                ) : null}
              </button>
            );
          })}
        </div>
      </Section>

      <Section title="Orders" subtitle={`${visible.length} shown`}>
        {isLoading ? (
          <div className="liquid-glass flex items-center justify-center rounded-[24px] p-16">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
          </div>
        ) : visible.length === 0 ? (
          <EmptyState
            icon={<PackageCheck className="h-7 w-7" />}
            title={search ? "No matches" : "No delivery orders yet"}
            description={
              search
                ? "Try a different search."
                : "Click 'New draft' to create one."
            }
          />
        ) : (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            {visible.map((o) => (
              <DeliveryOrderCard
                key={o.id}
                order={o}
                selected={selectedId === o.id}
                onSelect={onSelectCard}
              />
            ))}
          </div>
        )}
      </Section>
    </>
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

function PillStrip<T extends string>({
  filters,
  value,
  onChange,
  counts,
}: {
  filters: { label: string; value: T }[];
  value: T;
  onChange: (next: T) => void;
  counts: Partial<Record<T, number>>;
}) {
  return (
    <div className="liquid-glass-subtle relative flex flex-wrap items-center gap-1.5 rounded-2xl p-2">
      {filters.map((f) => {
        const count = counts[f.value];
        const active = value === f.value;
        return (
          <button
            key={f.value}
            type="button"
            onClick={() => onChange(f.value)}
            className={cn(
              "press-feedback inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-[12px] font-medium transition-colors",
              active
                ? "bg-primary/15 text-primary"
                : "text-muted-foreground hover:bg-black/[0.04] dark:hover:bg-white/[0.05]"
            )}
          >
            {f.label}
            {count !== undefined && count > 0 ? (
              <span
                className={cn(
                  "rounded-full px-1.5 text-[10px]",
                  active ? "bg-primary/20" : "bg-black/[0.05] dark:bg-white/[0.08]"
                )}
              >
                {count}
              </span>
            ) : null}
          </button>
        );
      })}
    </div>
  );
}

function matchesSearch(text: string, search: string): boolean {
  if (search.trim().length === 0) return true;
  return text.toLowerCase().includes(search.trim().toLowerCase());
}

function filterAndSearchActions(
  list: ActionTemplateDto[],
  activeFilter: ActiveFilter,
  search: string
): ActionTemplateDto[] {
  return list.filter((t) => {
    if (activeFilter === "active" && !t.isActive) return false;
    if (activeFilter === "inactive" && t.isActive) return false;
    return matchesSearch(`${t.name} ${t.actionType}`, search);
  });
}

function filterAndSearchOrders(
  list: OrderTemplateDto[],
  activeFilter: ActiveFilter,
  search: string
): OrderTemplateDto[] {
  return list.filter((t) => {
    if (activeFilter === "active" && !t.isActive) return false;
    if (activeFilter === "inactive" && t.isActive) return false;
    return matchesSearch(`${t.name} ${t.description ?? ""}`, search);
  });
}

function countActive<T extends { isActive: boolean }>(
  list: T[]
): Record<ActiveFilter, number> {
  const active = list.filter((t) => t.isActive).length;
  return {
    all: list.length,
    active,
    inactive: list.length - active,
  };
}

function countByStatus(
  orders: DeliveryOrderListDto[]
): Partial<Record<OrderStatus, number>> {
  const out: Partial<Record<OrderStatus, number>> = {};
  for (const o of orders) {
    out[o.orderStatus] = (out[o.orderStatus] ?? 0) + 1;
  }
  return out;
}
