"use client";

import { useEffect } from "react";
import { useFieldArray, useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Plus } from "lucide-react";

import { actionTemplatesApi } from "@/lib/action-templates";
import { orderTemplatesApi } from "@/lib/order-templates";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import {
  createOrderTemplateFormSchema,
  missionFormToRequest,
  missionToForm,
  type CreateOrderTemplateFormValues,
  type OrderTemplateDto,
} from "@/types/order-template";

import { Button } from "@/components/ui/button";
import {
  Form,
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import { Textarea } from "@/components/ui/textarea";
import { MissionRow } from "./mission-row";

interface OrderTemplateFormProps {
  template: OrderTemplateDto | null;
  onSaved?: (id: string) => void;
  onCancel?: () => void;
}

const emptyDefaults: CreateOrderTemplateFormValues = {
  name: "",
  priority: 10,
  structureType: "sequence",
  transportOrderPriority: 10,
  missions: [
    { type: "MOVE", mapId: "", stationId: "" },
  ],
  appointVehicleKey: "",
  appointVehicleName: "",
  appointVehicleGroupKey: "",
  appointVehicleGroupName: "",
  appointQueueWaitArea: "",
  description: "",
};

export function OrderTemplateForm({
  template,
  onSaved,
  onCancel,
}: OrderTemplateFormProps) {
  const queryClient = useQueryClient();
  const isEdit = template !== null;

  const form = useForm<CreateOrderTemplateFormValues>({
    resolver: zodResolver(createOrderTemplateFormSchema),
    defaultValues: emptyDefaults,
  });

  const missions = useFieldArray({ control: form.control, name: "missions" });

  const templatesQuery = useQuery({
    queryKey: queryKeys.actionTemplates.list({ includeInactive: false }),
    queryFn: () => actionTemplatesApi.list({ includeInactive: false }),
  });

  // Sync the form state when the consumer swaps between create + edit.
  useEffect(() => {
    if (template) {
      form.reset({
        name: template.name,
        priority: template.priority,
        structureType: template.structureType,
        transportOrderPriority: template.transportOrderPriority,
        missions: template.missions.map(missionToForm),
        appointVehicleKey: template.appointVehicleKey ?? "",
        appointVehicleName: template.appointVehicleName ?? "",
        appointVehicleGroupKey: template.appointVehicleGroupKey ?? "",
        appointVehicleGroupName: template.appointVehicleGroupName ?? "",
        appointQueueWaitArea: template.appointQueueWaitArea ?? "",
        description: template.description ?? "",
      });
    } else {
      form.reset(emptyDefaults);
    }
  }, [template, form]);

  const mutation = useMutation({
    mutationFn: async (values: CreateOrderTemplateFormValues) => {
      const body = {
        name: values.name,
        priority: values.priority,
        transportOrder: {
          structureType: values.structureType || "sequence",
          priority: values.transportOrderPriority ?? values.priority,
          missions: values.missions.map(missionFormToRequest),
        },
        appointVehicleKey: values.appointVehicleKey?.trim() || undefined,
        appointVehicleName: values.appointVehicleName?.trim() || undefined,
        appointVehicleGroupKey: values.appointVehicleGroupKey?.trim() || undefined,
        appointVehicleGroupName: values.appointVehicleGroupName?.trim() || undefined,
        appointQueueWaitArea: values.appointQueueWaitArea?.trim() || undefined,
        description: values.description?.trim() || undefined,
      };
      if (isEdit && template) {
        await orderTemplatesApi.update(template.id, body);
        return template.id;
      }
      return orderTemplatesApi.create(body);
    },
    onSuccess: (id) => {
      toast.success(`${isEdit ? "Updated" : "Created"} ${form.getValues("name")}`);
      queryClient.invalidateQueries({ queryKey: queryKeys.orderTemplates.all });
      onSaved?.(id);
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Save failed";
      toast.error(message);
    },
  });

  return (
    <Form {...form}>
      <form
        onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
        className="space-y-5"
      >
        <div className="grid grid-cols-2 gap-3">
          <FormField
            control={form.control}
            name="name"
            render={({ field }) => (
              <FormItem className="col-span-2">
                <FormLabel>Name</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    placeholder="MAIN_ROUTE_DEMO"
                    disabled={isEdit}
                  />
                </FormControl>
                <FormDescription>
                  {isEdit ? "Name is immutable." : "Unique within the catalog."}
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="priority"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Order priority</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="transportOrderPriority"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Transport priority</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormDescription>
                  Defaults to the order priority above.
                </FormDescription>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <Separator />

        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <p className="text-[13px] font-semibold tracking-tight">Missions</p>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() =>
                missions.append({ type: "MOVE", mapId: "", stationId: "" })
              }
              className="press-feedback rounded-full text-primary hover:bg-primary/10"
            >
              <Plus className="h-3.5 w-3.5" strokeWidth={2.5} />
              Add mission
            </Button>
          </div>
          {missions.fields.length === 0 ? (
            <p className="rounded-xl border border-dashed border-black/[0.10] p-5 text-center text-[12px] text-muted-foreground dark:border-white/10">
              No missions yet. Add at least one MOVE or ACT mission.
            </p>
          ) : (
            <div className="space-y-2">
              {missions.fields.map((f, i) => (
                <MissionRow
                  key={f.id}
                  index={i}
                  fieldArray={missions}
                  templates={templatesQuery.data ?? []}
                  templatesLoading={templatesQuery.isLoading}
                />
              ))}
            </div>
          )}
          {form.formState.errors.missions?.root ? (
            <p className="text-sm text-destructive">
              {form.formState.errors.missions.root.message}
            </p>
          ) : null}
        </div>

        <Separator />

        <div className="grid grid-cols-2 gap-3">
          <FormField
            control={form.control}
            name="appointVehicleKey"
            render={({ field }) => (
              <FormItem>
                <FormLabel>appointVehicleKey</FormLabel>
                <FormControl>
                  <Input {...field} placeholder="(empty = auto)" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="appointVehicleName"
            render={({ field }) => (
              <FormItem>
                <FormLabel>appointVehicleName</FormLabel>
                <FormControl>
                  <Input {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="appointVehicleGroupKey"
            render={({ field }) => (
              <FormItem>
                <FormLabel>appointVehicleGroupKey</FormLabel>
                <FormControl>
                  <Input {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="appointVehicleGroupName"
            render={({ field }) => (
              <FormItem>
                <FormLabel>appointVehicleGroupName</FormLabel>
                <FormControl>
                  <Input {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="appointQueueWaitArea"
            render={({ field }) => (
              <FormItem className="col-span-2">
                <FormLabel>appointQueueWaitArea</FormLabel>
                <FormControl>
                  <Input {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <FormField
          control={form.control}
          name="description"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Description (optional)</FormLabel>
              <FormControl>
                <Textarea {...field} rows={2} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

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
            className="liquid-pill-primary liquid-iridescent rounded-full px-5 font-medium"
          >
            {mutation.isPending
              ? "Saving…"
              : isEdit
              ? "Save changes"
              : "Create template"}
          </Button>
        </div>
      </form>
    </Form>
  );
}
