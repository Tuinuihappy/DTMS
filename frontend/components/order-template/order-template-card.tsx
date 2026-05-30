"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Library, Pencil, Play, Power, Trash2 } from "lucide-react";
import { toast } from "sonner";

import { orderTemplatesApi } from "@/lib/order-templates";
import { queryKeys } from "@/lib/query-keys";
import { ApiError } from "@/lib/api";
import type { OrderTemplateDto } from "@/types/order-template";

import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { ConfirmDestructive } from "@/components/shared/confirm-destructive";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

interface OrderTemplateCardProps {
  template: OrderTemplateDto;
  selected: boolean;
  onSelect: (id: string) => void;
  onInstantiate: (t: OrderTemplateDto) => void;
}

// Card variant for the Adobe-style grid. Differences vs the
// ActionTemplate card: includes a primary "Run" CTA (Adobe's "Open"
// equivalent), shows mission count + priority instead of params.
export function OrderTemplateCard({
  template,
  selected,
  onSelect,
  onInstantiate,
}: OrderTemplateCardProps) {
  const queryClient = useQueryClient();

  const toggleActive = useMutation({
    mutationFn: () =>
      template.isActive
        ? orderTemplatesApi.deactivate(template.id)
        : orderTemplatesApi.activate(template.id),
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
    mutationFn: () => orderTemplatesApi.delete(template.id),
    onSuccess: () => {
      toast.success(`Deleted ${template.name}`);
      queryClient.invalidateQueries({ queryKey: queryKeys.orderTemplates.all });
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
        selected && "ring-2 ring-primary/35",
        !template.isActive && "opacity-55"
      )}
    >
      <div className="relative z-[2] flex items-start justify-between gap-2">
        <div
          className="liquid-puck flex h-9 w-9 items-center justify-center rounded-2xl"
          style={
            {
              ["--tint" as string]:
                "color-mix(in oklch, oklch(0.70 0.18 290) 80%, white)",
            } as React.CSSProperties
          }
        >
          <Library
            className="relative z-[2] h-3.5 w-3.5 text-white"
            strokeWidth={2.25}
          />
        </div>
        <div className="flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
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
            description="This cannot be undone."
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
          {template.missions.length} mission
          {template.missions.length === 1 ? "" : "s"} · priority{" "}
          {template.priority}
        </p>
        {template.description ? (
          <p className="line-clamp-2 text-[11px] text-muted-foreground/80">
            {template.description}
          </p>
        ) : null}
      </div>

      <div className="relative z-[2] mt-3 flex items-center gap-2">
        <Button
          size="sm"
          variant="ghost"
          onClick={() => onSelect(template.id)}
          className="press-feedback h-7 flex-1 rounded-full bg-black/[0.04] text-[12px] font-medium hover:bg-black/[0.07] dark:bg-white/[0.06] dark:hover:bg-white/[0.10]"
        >
          <Pencil className="h-3 w-3" strokeWidth={2.25} />
          Edit
        </Button>
        <Button
          size="sm"
          onClick={() => onInstantiate(template)}
          className="liquid-pill-primary h-7 rounded-full px-3 text-[12px] font-medium"
        >
          <Play className="h-3 w-3 fill-current" strokeWidth={0} />
          Run
        </Button>
      </div>
    </div>
  );
}
