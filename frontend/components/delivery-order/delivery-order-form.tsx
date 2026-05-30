"use client";

import { useFieldArray, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus } from "lucide-react";

import { deliveryOrdersApi } from "@/lib/delivery-orders";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import {
  PRIORITY_VALUES,
  blankItem,
  createDraftDeliveryOrderFormSchema,
  formToCreateRequest,
  type CreateDraftDeliveryOrderFormValues,
} from "@/types/delivery-order";

import { Button } from "@/components/ui/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import { Textarea } from "@/components/ui/textarea";

import { ItemRowForm } from "./item-row-form";

interface DeliveryOrderFormProps {
  onSaved?: (orderId: string) => void;
  onCancel?: () => void;
}

const emptyDefaults: CreateDraftDeliveryOrderFormValues = {
  orderRef: "",
  priority: "Normal",
  serviceWindow: { earliestUtc: "", latestUtc: "" },
  requestedBy: "",
  notes: "",
  items: [blankItem],
};

// Create-draft form. Mirrors the OrderTemplateForm pattern (RHF +
// zodResolver + useFieldArray + shadcn Form primitives) — the
// implementation differences are the schema and the wire-format
// translation in formToCreateRequest.
export function DeliveryOrderForm({
  onSaved,
  onCancel,
}: DeliveryOrderFormProps) {
  const queryClient = useQueryClient();

  const form = useForm<CreateDraftDeliveryOrderFormValues>({
    resolver: zodResolver(createDraftDeliveryOrderFormSchema),
    defaultValues: emptyDefaults,
  });

  const items = useFieldArray({ control: form.control, name: "items" });

  const mutation = useMutation({
    mutationFn: async (values: CreateDraftDeliveryOrderFormValues) => {
      const body = formToCreateRequest(values);
      return deliveryOrdersApi.create(body);
    },
    onSuccess: (order) => {
      toast.success(`Draft ${order.orderRef} created`);
      queryClient.invalidateQueries({ queryKey: queryKeys.deliveryOrders.all });
      onSaved?.(order.id);
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Create failed";
      toast.error(message);
    },
  });

  return (
    <Form {...form}>
      <form
        onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
        className="space-y-5"
      >
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <FormField
            control={form.control}
            name="orderRef"
            render={({ field }) => (
              <FormItem className="sm:col-span-2">
                <FormLabel>Order ref</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    placeholder="PO-2026-0001"
                    autoComplete="off"
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />

          <FormField
            control={form.control}
            name="priority"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Priority</FormLabel>
                <Select onValueChange={field.onChange} value={field.value}>
                  <FormControl>
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {PRIORITY_VALUES.map((p) => (
                      <SelectItem key={p} value={p}>
                        {p}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />

          <FormField
            control={form.control}
            name="requestedBy"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Requested by (optional)</FormLabel>
                <FormControl>
                  <Input {...field} placeholder="dept / team" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />

          <FormField
            control={form.control}
            name="serviceWindow.earliestUtc"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Earliest (UTC)</FormLabel>
                <FormControl>
                  <Input type="datetime-local" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="serviceWindow.latestUtc"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Latest (UTC)</FormLabel>
                <FormControl>
                  <Input type="datetime-local" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <FormField
          control={form.control}
          name="notes"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Notes (optional)</FormLabel>
              <FormControl>
                <Textarea
                  {...field}
                  rows={2}
                  placeholder="Operator notes for this order…"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <Separator className="bg-black/[0.06] dark:bg-white/10" />

        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <p className="text-[13px] font-semibold tracking-tight">Items</p>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => items.append(blankItem)}
              className="press-feedback rounded-full text-primary hover:bg-primary/10"
            >
              <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
              Add item
            </Button>
          </div>
          {items.fields.length === 0 ? (
            <p className="rounded-xl border border-dashed border-black/[0.10] p-5 text-center text-[12px] text-muted-foreground dark:border-white/10">
              No items yet. Add at least one before submitting.
            </p>
          ) : (
            <div className="space-y-3">
              {items.fields.map((f, i) => (
                <ItemRowForm key={f.id} index={i} fieldArray={items} />
              ))}
            </div>
          )}
          {form.formState.errors.items?.root ? (
            <p className="text-[12px] text-destructive">
              {form.formState.errors.items.root.message}
            </p>
          ) : null}
        </div>

        <div className="flex items-center justify-end gap-2">
          {onCancel ? (
            <Button
              type="button"
              variant="ghost"
              onClick={onCancel}
              disabled={mutation.isPending}
              className="press-feedback rounded-full px-5 font-medium"
            >
              Cancel
            </Button>
          ) : null}
          <Button
            type="submit"
            disabled={mutation.isPending}
            className="liquid-pill-primary rounded-full px-5 font-medium"
          >
            {mutation.isPending ? "Saving…" : "Create draft"}
          </Button>
        </div>
      </form>
    </Form>
  );
}
