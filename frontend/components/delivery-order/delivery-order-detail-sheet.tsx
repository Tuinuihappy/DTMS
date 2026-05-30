"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  CheckCircle2,
  Loader2,
  Pause,
  Play,
  Send,
  X,
  XCircle,
} from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ConfirmDestructive } from "@/components/shared/confirm-destructive";

import { deliveryOrdersApi } from "@/lib/delivery-orders";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import {
  TERMINAL_STATUSES,
  formatEnumLabel,
  type ItemDto,
  type LifecycleResult,
  type OrderStatus,
} from "@/types/delivery-order";

import { StatusBadge } from "./status-badge";

interface DeliveryOrderDetailSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  orderId: string | null;
}

export function DeliveryOrderDetailSheet({
  open,
  onOpenChange,
  orderId,
}: DeliveryOrderDetailSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="liquid-glass !w-full overflow-y-auto rounded-l-[24px] !border-l-0 p-0 data-[side=right]:sm:!max-w-2xl"
      >
        <div className="relative z-[2] flex h-full flex-col">
          {orderId ? <DetailBody orderId={orderId} /> : <EmptyHeader />}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function EmptyHeader() {
  return (
    <SheetHeader className="border-b border-black/[0.06] px-6 py-5 dark:border-white/10">
      <SheetTitle>No order selected</SheetTitle>
      <SheetDescription>Pick an order from the grid to inspect.</SheetDescription>
    </SheetHeader>
  );
}

function DetailBody({ orderId }: { orderId: string }) {
  const queryClient = useQueryClient();

  const orderQuery = useQuery({
    queryKey: queryKeys.deliveryOrders.detail(orderId),
    queryFn: () => deliveryOrdersApi.get(orderId),
  });
  const itemsQuery = useQuery({
    queryKey: queryKeys.deliveryOrders.items(orderId),
    queryFn: () => deliveryOrdersApi.items(orderId),
  });

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: queryKeys.deliveryOrders.all });
  }

  function handleWarnings(result: LifecycleResult) {
    for (const w of result.warnings) {
      toast.warning(w.message, {
        description: `${w.field} · ${w.code}`,
      });
    }
  }

  const submit = useMutation({
    mutationFn: () => deliveryOrdersApi.submit(orderId),
    onSuccess: (r) => {
      toast.success("Submitted");
      handleWarnings(r);
      invalidate();
    },
    onError: errorToast("submit"),
  });

  const confirm = useMutation({
    mutationFn: () => deliveryOrdersApi.confirm(orderId),
    onSuccess: (r) => {
      toast.success("Confirmed");
      handleWarnings(r);
      invalidate();
    },
    onError: errorToast("confirm"),
  });

  const hold = useMutation({
    mutationFn: (reason: string) => deliveryOrdersApi.hold(orderId, reason),
    onSuccess: () => {
      toast.success("On hold");
      invalidate();
    },
    onError: errorToast("hold"),
  });

  const release = useMutation({
    mutationFn: () => deliveryOrdersApi.release(orderId),
    onSuccess: (r) => {
      toast.success("Released");
      handleWarnings(r);
      invalidate();
    },
    onError: errorToast("release"),
  });

  const reject = useMutation({
    mutationFn: (reason: string) => deliveryOrdersApi.reject(orderId, reason),
    onSuccess: () => {
      toast.success("Rejected");
      invalidate();
    },
    onError: errorToast("reject"),
  });

  const cancel = useMutation({
    mutationFn: (reason: string) => deliveryOrdersApi.cancel(orderId, reason),
    onSuccess: () => {
      toast.success("Cancelled");
      invalidate();
    },
    onError: errorToast("cancel"),
  });

  if (orderQuery.isLoading) {
    return (
      <div className="flex flex-1 items-center justify-center">
        <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
      </div>
    );
  }
  if (orderQuery.isError || !orderQuery.data) {
    return (
      <SheetHeader className="px-6 py-5">
        <SheetTitle>Couldn&apos;t load order</SheetTitle>
        <SheetDescription>
          {(orderQuery.error as Error)?.message ?? "Unknown error"}
        </SheetDescription>
      </SheetHeader>
    );
  }

  const order = orderQuery.data;
  const items = itemsQuery.data ?? order.items;
  const canAct = !TERMINAL_STATUSES.includes(order.orderStatus);

  return (
    <>
      <SheetHeader className="border-b border-black/[0.06] px-6 py-5 dark:border-white/10">
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0">
            <SheetTitle className="font-mono text-[17px] font-semibold tracking-tight">
              {order.orderRef}
            </SheetTitle>
            <SheetDescription>
              {order.sourceSystem} · created{" "}
              {new Date(order.createdDate).toLocaleString()}
            </SheetDescription>
          </div>
          <StatusBadge status={order.orderStatus} />
        </div>
      </SheetHeader>

      <div className="flex-1 overflow-y-auto p-6">
        <Tabs defaultValue="order" className="space-y-4">
          <TabsList className="liquid-glass-subtle h-9 rounded-full p-1">
            <TabsTrigger value="order" className="rounded-full px-4 text-[12px]">
              Order
            </TabsTrigger>
            <TabsTrigger
              value="items"
              className="rounded-full px-4 text-[12px]"
            >
              Items ({items.length})
            </TabsTrigger>
          </TabsList>

          <TabsContent value="order" className="space-y-3">
            <Detail label="Priority" value={formatEnumLabel(order.priority)} />
            <Detail
              label="Total items / weight"
              value={`${order.totalItems} · ${order.totalWeightKg.toFixed(2)} kg`}
            />
            <Detail
              label="Service window"
              value={
                <ServiceWindowSpan
                  earliest={order.serviceWindow?.earliestUtc}
                  latest={order.serviceWindow?.latestUtc}
                />
              }
            />
            {order.submittedAt ? (
              <Detail
                label="Submitted at"
                value={new Date(order.submittedAt).toLocaleString()}
              />
            ) : null}
            {order.requestedBy ? (
              <Detail label="Requested by" value={order.requestedBy} />
            ) : null}
            {order.notes ? (
              <Detail label="Notes" value={order.notes} />
            ) : null}
            {order.createdBy ? (
              <Detail label="Created by" value={order.createdBy} />
            ) : null}
          </TabsContent>

          <TabsContent value="items">
            <ItemsTable items={items} />
          </TabsContent>
        </Tabs>
      </div>

      {canAct ? (
        <div className="border-t border-black/[0.06] bg-white/40 px-6 py-4 backdrop-blur-md dark:border-white/10 dark:bg-white/[0.04]">
          <ActionRow
            status={order.orderStatus}
            isPending={
              submit.isPending ||
              confirm.isPending ||
              hold.isPending ||
              release.isPending ||
              reject.isPending ||
              cancel.isPending
            }
            onSubmit={() => submit.mutate()}
            onConfirm={() => confirm.mutate()}
            onHold={(reason) => hold.mutate(reason)}
            onRelease={() => release.mutate()}
            onReject={(reason) => reject.mutate(reason)}
            onCancel={(reason) => cancel.mutate(reason)}
          />
        </div>
      ) : null}
    </>
  );
}

function errorToast(label: string) {
  return (error: unknown) => {
    const message =
      error instanceof ApiError ? error.message : `${label} failed`;
    toast.error(message);
  };
}

function Detail({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[140px_1fr] gap-2 text-[13px]">
      <span className="text-muted-foreground">{label}</span>
      <span>{value}</span>
    </div>
  );
}

function ServiceWindowSpan({
  earliest,
  latest,
}: {
  earliest?: string | null;
  latest?: string | null;
}) {
  if (!earliest && !latest) {
    return <span className="text-muted-foreground/70">No window</span>;
  }
  const fmt = (s: string) => new Date(s).toLocaleString();
  return (
    <span>
      {earliest ? fmt(earliest) : "—"} → {latest ? fmt(latest) : "—"}
    </span>
  );
}

function ItemsTable({ items }: { items: ItemDto[] }) {
  if (items.length === 0) {
    return (
      <p className="rounded-xl border border-dashed border-black/[0.10] p-5 text-center text-[12px] text-muted-foreground dark:border-white/10">
        No items.
      </p>
    );
  }
  return (
    <div className="liquid-glass-subtle relative overflow-hidden rounded-xl">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-8">#</TableHead>
            <TableHead>Item</TableHead>
            <TableHead>Pickup → Drop</TableHead>
            <TableHead className="text-right">Qty</TableHead>
            <TableHead className="text-right">Weight</TableHead>
            <TableHead>Status</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((it) => (
            <TableRow key={it.id}>
              <TableCell className="text-muted-foreground">
                {it.itemSeq}
              </TableCell>
              <TableCell className="font-mono text-[12px]">{it.itemId}</TableCell>
              <TableCell className="text-[12px]">
                {it.pickupLocationCode} → {it.dropLocationCode}
              </TableCell>
              <TableCell className="text-right text-[12px]">
                {it.quantity.value} {it.quantity.uom}
              </TableCell>
              <TableCell className="text-right text-[12px]">
                {it.weightKg != null ? `${it.weightKg} kg` : "—"}
              </TableCell>
              <TableCell className="text-[11px] text-muted-foreground">
                {it.status}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

// ── Action row — visible buttons depend on current status ────────────────

interface ActionRowProps {
  status: OrderStatus;
  isPending: boolean;
  onSubmit: () => void;
  onConfirm: () => void;
  onHold: (reason: string) => void;
  onRelease: () => void;
  onReject: (reason: string) => void;
  onCancel: (reason: string) => void;
}

function ActionRow({
  status,
  isPending,
  onSubmit,
  onConfirm,
  onHold,
  onRelease,
  onReject,
  onCancel,
}: ActionRowProps) {
  // Which lifecycle buttons make sense per status. The backend enforces
  // these too; this is the UI's first line of defence.
  const canSubmit = status === "DRAFT";
  const canConfirm = status === "SUBMITTED" || status === "VALIDATED";
  const canHold = [
    "CONFIRMED",
    "VALIDATED",
    "PLANNING",
    "PLANNED",
    "DISPATCHED",
  ].includes(status);
  const canRelease = status === "HELD";
  const canReject = ["SUBMITTED", "VALIDATED", "CONFIRMED"].includes(status);

  return (
    <div className="flex flex-wrap items-center justify-end gap-2">
      {canSubmit ? (
        <Button
          size="sm"
          onClick={onSubmit}
          disabled={isPending}
          className="liquid-pill-primary rounded-full px-4 text-[12px] font-medium"
        >
          <Send className="h-3.5 w-3.5" strokeWidth={2.25} />
          Submit
        </Button>
      ) : null}
      {canConfirm ? (
        <Button
          size="sm"
          onClick={onConfirm}
          disabled={isPending}
          className="liquid-pill-primary rounded-full px-4 text-[12px] font-medium"
        >
          <CheckCircle2 className="h-3.5 w-3.5" strokeWidth={2.25} />
          Confirm
        </Button>
      ) : null}
      {canHold ? (
        <ReasonPromptButton
          label="Hold"
          icon={<Pause className="h-3.5 w-3.5" strokeWidth={2.25} />}
          onConfirm={onHold}
          disabled={isPending}
        />
      ) : null}
      {canRelease ? (
        <Button
          size="sm"
          onClick={onRelease}
          disabled={isPending}
          className="liquid-pill-primary rounded-full px-4 text-[12px] font-medium"
        >
          <Play className="h-3.5 w-3.5 fill-current" strokeWidth={0} />
          Release
        </Button>
      ) : null}
      {canReject ? (
        <ReasonPromptButton
          label="Reject"
          destructive
          icon={<XCircle className="h-3.5 w-3.5" strokeWidth={2.25} />}
          onConfirm={onReject}
          disabled={isPending}
        />
      ) : null}
      <ConfirmDestructive
        trigger={
          <Button
            size="sm"
            variant="ghost"
            disabled={isPending}
            className="press-feedback rounded-full px-4 text-[12px] font-medium text-destructive hover:bg-destructive/10"
          >
            <X className="h-3.5 w-3.5" strokeWidth={2.25} />
            Cancel
          </Button>
        }
        title="Cancel this order?"
        description="The order will be marked Cancelled. This cannot be undone."
        confirmLabel="Cancel order"
        onConfirm={() => onCancel("Cancelled via UI")}
      />
    </div>
  );
}

function ReasonPromptButton({
  label,
  icon,
  onConfirm,
  destructive,
  disabled,
}: {
  label: string;
  icon: React.ReactNode;
  onConfirm: (reason: string) => void;
  destructive?: boolean;
  disabled?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");

  return (
    <>
      <Button
        size="sm"
        variant="ghost"
        disabled={disabled}
        onClick={() => setOpen(true)}
        className={
          destructive
            ? "press-feedback rounded-full px-4 text-[12px] font-medium text-destructive hover:bg-destructive/10"
            : "press-feedback rounded-full px-4 text-[12px] font-medium hover:bg-black/[0.05] dark:hover:bg-white/[0.06]"
        }
      >
        {icon}
        {label}
      </Button>
      <Sheet open={open} onOpenChange={setOpen}>
        <SheetContent
          side="bottom"
          className="liquid-glass !h-auto rounded-t-[24px] !border-t-0 p-0"
        >
          <div className="relative z-[2] flex flex-col gap-4 p-6">
            <div>
              <h3 className="text-[15px] font-semibold tracking-tight">
                {label} — reason
              </h3>
              <p className="text-[12px] text-muted-foreground">
                Give a short justification for the audit trail.
              </p>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="reason-input">Reason</Label>
              <Input
                id="reason-input"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                autoFocus
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button
                variant="ghost"
                onClick={() => setOpen(false)}
                className="press-feedback rounded-full px-4 text-[12px]"
              >
                Cancel
              </Button>
              <Button
                disabled={reason.trim().length === 0}
                onClick={() => {
                  onConfirm(reason.trim());
                  setReason("");
                  setOpen(false);
                }}
                className="liquid-pill-primary rounded-full px-4 text-[12px] font-medium"
              >
                {label}
              </Button>
            </div>
          </div>
        </SheetContent>
      </Sheet>
    </>
  );
}
