"use client";

import { Pencil, Power, Trash2 } from "lucide-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { ConfirmDestructive } from "@/components/shared/confirm-destructive";

import { actionTemplatesApi } from "@/lib/action-templates";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import type { ActionTemplateDto } from "@/types/action-template";
import { cn } from "@/lib/utils";

interface ActionTemplateRowProps {
  template: ActionTemplateDto;
  onEdit: (template: ActionTemplateDto) => void;
}

export function ActionTemplateRow({ template, onEdit }: ActionTemplateRowProps) {
  const queryClient = useQueryClient();

  const toggleActive = useMutation({
    mutationFn: () =>
      template.isActive
        ? actionTemplatesApi.deactivate(template.id)
        : actionTemplatesApi.activate(template.id),
    onMutate: async () => {
      // Optimistic: flip every cached list entry, then roll back on error.
      await queryClient.cancelQueries({ queryKey: queryKeys.actionTemplates.all });
      const previous = queryClient.getQueriesData<ActionTemplateDto[]>({
        queryKey: queryKeys.actionTemplates.all,
      });
      for (const [key, list] of previous) {
        if (!list) continue;
        queryClient.setQueryData<ActionTemplateDto[]>(
          key,
          list.map((t) =>
            t.id === template.id ? { ...t, isActive: !t.isActive } : t
          )
        );
      }
      return { previous };
    },
    onError: (error: unknown, _vars, context) => {
      if (context?.previous) {
        for (const [key, data] of context.previous) {
          queryClient.setQueryData(key, data);
        }
      }
      const message =
        error instanceof ApiError ? error.message : "Failed to toggle";
      toast.error(message);
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.actionTemplates.all });
    },
  });

  const remove = useMutation({
    mutationFn: () => actionTemplatesApi.delete(template.id),
    onSuccess: () => {
      toast.success(`Deleted ${template.name}`);
      queryClient.invalidateQueries({ queryKey: queryKeys.actionTemplates.all });
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Delete failed";
      toast.error(message);
    },
  });

  return (
    <div
      className={cn(
        // Apple-style row: subtle hover via shadow + bg, no lift.
        // Less motion = more "settled" / less playful.
        "group liquid-glass-subtle relative flex items-start justify-between gap-2 rounded-2xl p-4 transition-all duration-200",
        "hover:shadow-[0_6px_20px_-4px_rgba(0,0,0,0.10)]",
        !template.isActive && "opacity-55"
      )}
    >
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="truncate font-mono text-[13px] font-medium tracking-tight">
            {template.name}
          </span>
          {!template.isActive ? (
            <Badge
              variant="outline"
              className="border-black/[0.08] bg-black/[0.03] text-[10px] uppercase font-medium tracking-wide text-muted-foreground dark:border-white/10 dark:bg-white/[0.04]"
            >
              inactive
            </Badge>
          ) : null}
        </div>
        <div className="mt-1.5 text-[12px] text-muted-foreground">
          <span className="font-mono">{template.actionType}</span>
          <span className="mx-1.5 text-muted-foreground/50">·</span>
          <span className="font-mono">
            id={template.vendorActionId} p0={template.param0} p1={template.param1}
            {template.paramStr ? ` str=${template.paramStr}` : ""}
          </span>
        </div>
        {template.description ? (
          <p className="mt-1.5 line-clamp-2 text-[12px] text-muted-foreground/80">
            {template.description}
          </p>
        ) : null}
      </div>

      <div className="flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity group-hover:opacity-100">
        <Tooltip>
          <TooltipTrigger
            render={
              <Button
                size="icon"
                variant="ghost"
                onClick={() => onEdit(template)}
                aria-label="Edit"
                className="press-feedback rounded-full hover:bg-black/[0.05] dark:hover:bg-white/[0.08]"
              >
                <Pencil className="h-3.5 w-3.5" strokeWidth={2} />
              </Button>
            }
          />
          <TooltipContent>Edit</TooltipContent>
        </Tooltip>

        <Tooltip>
          <TooltipTrigger
            render={
              <Button
                size="icon"
                variant="ghost"
                onClick={() => toggleActive.mutate()}
                disabled={toggleActive.isPending}
                aria-label={template.isActive ? "Deactivate" : "Activate"}
                className="press-feedback rounded-full hover:bg-black/[0.05] dark:hover:bg-white/[0.08]"
              >
                <Power className="h-3.5 w-3.5" strokeWidth={2} />
              </Button>
            }
          />
          <TooltipContent>
            {template.isActive ? "Deactivate" : "Activate"}
          </TooltipContent>
        </Tooltip>

        <ConfirmDestructive
          trigger={
            <Button
              size="icon"
              variant="ghost"
              aria-label="Delete"
              className="press-feedback rounded-full text-destructive hover:bg-destructive/10 hover:text-destructive"
            >
              <Trash2 className="h-3.5 w-3.5" strokeWidth={2} />
            </Button>
          }
          title={`Delete ${template.name}?`}
          description="OrderTemplates that reference this name will fail at instantiate time. This cannot be undone."
          onConfirm={() => remove.mutateAsync()}
        />
      </div>
    </div>
  );
}
