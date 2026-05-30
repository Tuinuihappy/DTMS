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
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-[11px] font-semibold uppercase tracking-[0.06em] text-muted-foreground">
          Saved templates
        </p>
        <label className="flex items-center gap-1.5 text-[12px] text-muted-foreground">
          <Checkbox
            checked={includeInactive}
            onCheckedChange={(v) => onIncludeInactiveChange(v === true)}
          />
          Show inactive
        </label>
      </div>

      <ScrollArea className="liquid-glass-subtle h-52 rounded-2xl">
        <div className="space-y-1 p-2.5">
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
                  "press-feedback group flex w-full items-center gap-2 rounded-xl border border-transparent px-3 py-2.5 text-left",
                  "hover:bg-card dark:hover:bg-white/[0.05]",
                  selectedId === t.id &&
                    "bg-primary/10 ring-1 ring-primary/30 hover:bg-primary/10 dark:bg-primary/15 dark:ring-primary/40",
                  !t.isActive && "opacity-55"
                )}
              >
                <ChevronRight
                  className={cn(
                    "h-3.5 w-3.5 transition-transform",
                    selectedId === t.id
                      ? "rotate-90 text-primary"
                      : "text-muted-foreground"
                  )}
                  strokeWidth={2.25}
                />
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span
                      className={cn(
                        "truncate font-mono text-[12px] font-medium tracking-tight",
                        selectedId === t.id && "text-primary"
                      )}
                    >
                      {t.name}
                    </span>
                    {!t.isActive ? (
                      <Badge
                        variant="outline"
                        className="border-black/[0.08] bg-black/[0.03] text-[10px] uppercase font-medium tracking-wide text-muted-foreground dark:border-white/10 dark:bg-white/[0.04]"
                      >
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
                  className="flex shrink-0 items-center gap-1.5 opacity-0 transition-opacity group-hover:opacity-100"
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
                          onClick={(e) => {
                            e.stopPropagation();
                            onInstantiate(t);
                          }}
                          aria-label="Instantiate"
                          className="liquid-puck liquid-puck-primary size-7 rounded-full hover:bg-transparent"
                        >
                          <Play className="relative z-[2] h-3 w-3 fill-current" strokeWidth={0} />
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
                          className="liquid-puck size-7 rounded-full text-foreground hover:bg-transparent"
                        >
                          <Power className="relative z-[2] h-3 w-3" strokeWidth={2.25} />
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
                        className="liquid-puck size-7 rounded-full text-destructive hover:bg-transparent"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <Trash2 className="relative z-[2] h-3 w-3" strokeWidth={2.25} />
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
