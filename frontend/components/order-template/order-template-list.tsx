"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronRight, Pencil, Play, Power, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { orderTemplatesApi } from "@/lib/order-templates";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import type { OrderTemplateDto } from "@/types/order-template";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { ScrollArea } from "@/components/ui/scroll-area";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { ConfirmDestructive } from "@/components/shared/confirm-destructive";
import { EmptyState } from "@/components/shared/empty-state";
import { cn } from "@/lib/utils";

interface OrderTemplateListProps {
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  onInstantiate: (template: OrderTemplateDto) => void;
  includeInactive: boolean;
  onIncludeInactiveChange: (next: boolean) => void;
}

export function OrderTemplateList({
  selectedId,
  onSelect,
  onInstantiate,
  includeInactive,
  onIncludeInactiveChange,
}: OrderTemplateListProps) {
  const queryClient = useQueryClient();

  const query = useQuery({
    queryKey: queryKeys.orderTemplates.list({ includeInactive }),
    queryFn: () => orderTemplatesApi.list({ includeInactive }),
  });

  const toggleActive = useMutation({
    mutationFn: async (t: OrderTemplateDto) =>
      t.isActive
        ? orderTemplatesApi.deactivate(t.id)
        : orderTemplatesApi.activate(t.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.orderTemplates.all });
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Failed to toggle";
      toast.error(message);
    },
  });

  const remove = useMutation({
    mutationFn: (id: string) => orderTemplatesApi.delete(id),
    onSuccess: (_data, deletedId) => {
      toast.success("Template deleted");
      if (deletedId === selectedId) onSelect(null);
      queryClient.invalidateQueries({ queryKey: queryKeys.orderTemplates.all });
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Delete failed";
      toast.error(message);
    },
  });

  const templates = query.data ?? [];

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Saved templates
        </p>
        <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Checkbox
            checked={includeInactive}
            onCheckedChange={(v) => onIncludeInactiveChange(v === true)}
          />
          Show inactive
        </label>
      </div>

      <ScrollArea className="h-44 rounded-md border">
        <div className="space-y-1 p-2">
          {query.isLoading ? (
            <p className="p-3 text-xs text-muted-foreground">Loading…</p>
          ) : query.isError ? (
            <EmptyState
              title="Couldn't load templates"
              description={(query.error as Error)?.message ?? "Unknown error"}
            />
          ) : templates.length === 0 ? (
            <EmptyState
              title="No templates yet"
              description="Build the first one in the composer to the right."
            />
          ) : (
            templates.map((t) => (
              <button
                key={t.id}
                type="button"
                onClick={() => onSelect(t.id)}
                className={cn(
                  "group flex w-full items-center gap-2 rounded-md border border-transparent px-2 py-1.5 text-left transition hover:bg-accent",
                  selectedId === t.id && "border-border bg-accent",
                  !t.isActive && "opacity-60"
                )}
              >
                <ChevronRight
                  className={cn(
                    "h-3.5 w-3.5 text-muted-foreground transition-transform",
                    selectedId === t.id && "rotate-90"
                  )}
                />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="truncate font-mono text-xs font-medium">
                      {t.name}
                    </span>
                    {!t.isActive ? (
                      <Badge variant="outline" className="text-[10px] uppercase">
                        inactive
                      </Badge>
                    ) : null}
                  </div>
                  <p className="truncate text-[11px] text-muted-foreground">
                    {t.missions.length} mission{t.missions.length === 1 ? "" : "s"}
                    {" · "}prio {t.priority}
                  </p>
                </div>
                <div
                  className="flex shrink-0 items-center gap-0.5 opacity-0 transition-opacity group-hover:opacity-100"
                  onClick={(e) => e.stopPropagation()}
                >
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          size="icon"
                          variant="ghost"
                          onClick={(e) => {
                            e.stopPropagation();
                            onSelect(t.id);
                          }}
                          aria-label="Edit"
                        >
                          <Pencil className="h-3 w-3" />
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
                          onClick={(e) => {
                            e.stopPropagation();
                            onInstantiate(t);
                          }}
                          aria-label="Instantiate"
                        >
                          <Play className="h-3 w-3" />
                        </Button>
                      }
                    />
                    <TooltipContent>Instantiate</TooltipContent>
                  </Tooltip>
                  <Tooltip>
                    <TooltipTrigger
                      render={
                        <Button
                          size="icon"
                          variant="ghost"
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleActive.mutate(t);
                          }}
                          disabled={toggleActive.isPending}
                          aria-label={t.isActive ? "Deactivate" : "Activate"}
                        >
                          <Power className="h-3 w-3" />
                        </Button>
                      }
                    />
                    <TooltipContent>
                      {t.isActive ? "Deactivate" : "Activate"}
                    </TooltipContent>
                  </Tooltip>
                  <ConfirmDestructive
                    trigger={
                      <Button
                        size="icon"
                        variant="ghost"
                        aria-label="Delete"
                        className="text-destructive hover:text-destructive"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <Trash2 className="h-3 w-3" />
                      </Button>
                    }
                    title={`Delete ${t.name}?`}
                    description="This cannot be undone."
                    onConfirm={() => remove.mutateAsync(t.id)}
                  />
                </div>
              </button>
            ))
          )}
        </div>
      </ScrollArea>
    </div>
  );
}
