"use client";

import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { actionTemplatesApi } from "@/lib/action-templates";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import {
  actionParametersToForm,
  createActionTemplateFormSchema,
  formToActionParameters,
  type ActionTemplateDto,
  type CreateActionTemplateFormValues,
} from "@/types/action-template";

import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
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
import { Textarea } from "@/components/ui/textarea";

interface ActionTemplateFormProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  // null = create mode; an existing template = edit mode
  template: ActionTemplateDto | null;
}

const emptyDefaults: CreateActionTemplateFormValues = {
  actionName: "",
  actionType: "standardRobotsCustom",
  id: 0,
  param0: 0,
  param1: 0,
  paramStr: "",
  description: "",
};

export function ActionTemplateForm({
  open,
  onOpenChange,
  template,
}: ActionTemplateFormProps) {
  const isEdit = template !== null;
  const queryClient = useQueryClient();

  const form = useForm<CreateActionTemplateFormValues>({
    resolver: zodResolver(createActionTemplateFormSchema),
    defaultValues: emptyDefaults,
  });

  // Reset whenever the dialog (re)opens or the underlying template changes.
  // Without this, switching from "edit LIFT_PALLET" → close → "+ New" would
  // show stale values from the previous edit.
  useEffect(() => {
    if (!open) return;
    if (template) {
      const params = actionParametersToForm(template);
      form.reset({
        actionName: template.name,
        actionType: template.actionType,
        id: params.id,
        param0: params.param0,
        param1: params.param1,
        paramStr: params.paramStr,
        description: template.description ?? "",
      });
    } else {
      form.reset(emptyDefaults);
    }
  }, [open, template, form]);

  const mutation = useMutation({
    mutationFn: async (values: CreateActionTemplateFormValues) => {
      const actionParameters = formToActionParameters(values);
      const body = {
        actionName: values.actionName,
        actionType: values.actionType?.trim() || undefined,
        actionParameters,
        description: values.description?.trim() || undefined,
      };
      if (isEdit && template) {
        await actionTemplatesApi.update(template.id, {
          actionType: body.actionType,
          actionParameters,
          description: body.description,
        });
        return { id: template.id, name: values.actionName };
      }
      const id = await actionTemplatesApi.create(body);
      return { id, name: values.actionName };
    },
    onSuccess: ({ name }) => {
      toast.success(`${isEdit ? "Updated" : "Created"} ${name}`);
      queryClient.invalidateQueries({ queryKey: queryKeys.actionTemplates.all });
      onOpenChange(false);
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Something went wrong";
      toast.error(message);
    },
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {isEdit ? "Edit ActionTemplate" : "New ActionTemplate"}
          </DialogTitle>
          <DialogDescription>
            A named recipe for a single RIOT3 ACT mission. OrderTemplates
            reference these by name.
          </DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
            className="space-y-5"
          >
            <FormField
              control={form.control}
              name="actionName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Name</FormLabel>
                  <FormControl>
                    <Input
                      {...field}
                      placeholder="LIFT_PALLET"
                      autoComplete="off"
                      disabled={isEdit}
                    />
                  </FormControl>
                  <FormDescription>
                    {isEdit
                      ? "Name is immutable once created."
                      : "Unique, uppercase by convention."}
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="actionType"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Action type</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="standardRobotsCustom" />
                  </FormControl>
                  <FormDescription>
                    Defaults to <code>STD</code> on the backend when blank.
                  </FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <div className="grid grid-cols-3 gap-3">
              <FormField
                control={form.control}
                name="id"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>id</FormLabel>
                    <FormControl>
                      <Input type="number" inputMode="numeric" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="param0"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>param0</FormLabel>
                    <FormControl>
                      <Input type="number" inputMode="numeric" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="param1"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>param1</FormLabel>
                    <FormControl>
                      <Input type="number" inputMode="numeric" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </div>

            <FormField
              control={form.control}
              name="paramStr"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>param_str (optional)</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="description"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Description (optional)</FormLabel>
                  <FormControl>
                    <Textarea
                      {...field}
                      rows={3}
                      placeholder="What is this template for?"
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <DialogFooter>
              <Button
                type="button"
                variant="ghost"
                onClick={() => onOpenChange(false)}
                disabled={mutation.isPending}
                className="press-feedback rounded-full px-5 font-medium"
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={mutation.isPending}
                className="press-feedback rounded-full bg-primary px-5 font-medium text-primary-foreground shadow-sm shadow-primary/20 hover:bg-primary/90"
              >
                {mutation.isPending
                  ? "Saving…"
                  : isEdit
                  ? "Save changes"
                  : "Create"}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
