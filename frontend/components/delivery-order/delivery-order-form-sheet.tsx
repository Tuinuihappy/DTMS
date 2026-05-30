"use client";

import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";

import { DeliveryOrderForm } from "./delivery-order-form";

interface DeliveryOrderFormSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  // Called after a successful create with the new order id; the page
  // typically responds by opening the detail Sheet on that order.
  onCreated?: (orderId: string) => void;
}

// Right-side Sheet wrapping the create-draft form. The base-ui Dialog
// primitive used by Sheet doesn't expose width as a side-prop variant,
// so we override `data-[side=right]:sm:max-w-xl` here to widen the
// default `sm:max-w-sm` (too tight for the items field-array).
export function DeliveryOrderFormSheet({
  open,
  onOpenChange,
  onCreated,
}: DeliveryOrderFormSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="liquid-glass !w-full overflow-y-auto rounded-l-[24px] !border-l-0 p-0 data-[side=right]:sm:!max-w-xl"
      >
        <div className="relative z-[2] flex h-full flex-col">
          <SheetHeader className="border-b border-black/[0.06] px-6 py-5 dark:border-white/10">
            <SheetTitle className="text-[17px] font-semibold tracking-tight">
              New delivery order
            </SheetTitle>
            <SheetDescription>
              Create a draft order with one or more items. You can submit
              and confirm it from the detail view after saving.
            </SheetDescription>
          </SheetHeader>

          <div className="flex-1 overflow-y-auto p-6">
            <DeliveryOrderForm
              onCancel={() => onOpenChange(false)}
              onSaved={(id) => {
                onCreated?.(id);
                onOpenChange(false);
              }}
            />
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
