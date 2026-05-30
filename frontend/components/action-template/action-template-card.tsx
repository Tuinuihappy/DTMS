"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Power, Sparkles, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { actionTemplatesApi } from "@/lib/action-templates";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import type { ActionTemplateDto } from "@/types/action-template";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDestructive } from "@/components/shared/confirm-destructive";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

interface ActionTemplateCardProps {
  template: ActionTemplateDto;
  onEdit: (t: ActionTemplateDto) => void;
}

// Card variant of ActionTemplate row — used in the Adobe-style grid.
// Behaviour is identical to the row (CRUD + optimistic toggle); only
// the layout changes: vertical card with a coloured icon chip at top,
// title + meta below, action pucks tucked in the top-right corner.
export function ActionTemplateCard({
  template,
  onEdit,
}: ActionTemplateCardProps) {
  const queryClient = useQueryClient();

  const toggleActive = useMutation({
    mutationFn: () =>
      template.isActive
        ? actionTemplatesApi.deactivate(template.id)
        : actionTemplatesApi.activate(template.id),
    onMutate: async () => {
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
        "group liquid-glass relative overflow-hidden rounded-2xl p-4 transition-all duration-200",
        "hover:shadow-[0_8px_28px_-4px_rgba(0,0,0,0.10)]",
        !template.isActive && "opacity-55"
      )}
    >
      <div className="relative z-[2] flex items-start justify-between gap-2">
        <div className="liquid-puck liquid-puck-primary flex h-9 w-9 items-center justify-center rounded-2xl">
          <Sparkles className="relative z-[2] h-3.5 w-3.5" strokeWidth={2.25} />
        </div>
        <div className="flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
          <Tooltip>
            <TooltipTrigger
              render={
                <Button
                  size="icon"
                  variant="ghost"
                  onClick={() => onEdit(template)}
                  aria-label="Edit"
                  className="liquid-puck size-7 rounded-full text-foreground hover:bg-transparent"
                >
                  <Pencil className="relative z-[2] h-3 w-3" strokeWidth={2.25} />
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
                  className="liquid-puck size-7 rounded-full text-foreground hover:bg-transparent"
                >
                  <Power className="relative z-[2] h-3 w-3" strokeWidth={2.25} />
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
                className="liquid-puck size-7 rounded-full text-destructive hover:bg-transparent"
              >
                <Trash2 className="relative z-[2] h-3 w-3" strokeWidth={2.25} />
              </Button>
            }
            title={`Delete ${template.name}?`}
            description="OrderTemplates that reference this name will fail at instantiate time. This cannot be undone."
            onConfirm={() => remove.mutateAsync()}
          />
        </div>
      </div>

      <div className="relative z-[2] mt-3 space-y-1">
        <div className="flex items-center gap-2">
          <h3 className="truncate font-mono text-[13px] font-semibold tracking-tight">
            {template.name}
          </h3>
          {!template.isActive ? (
            <Badge
              variant="outline"
              className="border-black/[0.08] bg-black/[0.03] text-[10px] uppercase font-medium tracking-wide text-muted-foreground dark:border-white/10 dark:bg-white/[0.04]"
            >
              inactive
            </Badge>
          ) : null}
        </div>
        <p className="truncate text-[12px] text-muted-foreground">
          {template.actionType}
        </p>
        <p className="truncate font-mono text-[11px] text-muted-foreground/80">
          id={template.vendorActionId} p0={template.param0} p1={template.param1}
          {template.paramStr ? ` str=${template.paramStr}` : ""}
        </p>
      </div>
    </div>
  );
}
