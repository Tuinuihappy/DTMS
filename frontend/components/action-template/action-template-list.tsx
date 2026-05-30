"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Inbox, Loader2, Plus, Search } from "lucide-react";

import { actionTemplatesApi } from "@/lib/action-templates";
import { queryKeys } from "@/lib/query-keys";
import type { ActionTemplateDto } from "@/types/action-template";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { EmptyState } from "@/components/shared/empty-state";
import { ActionTemplateForm } from "./action-template-form";
import { ActionTemplateRow } from "./action-template-row";

export function ActionTemplateList() {
  const [includeInactive, setIncludeInactive] = useState(false);
  const [search, setSearch] = useState("");
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<ActionTemplateDto | null>(null);

  const query = useQuery({
    queryKey: queryKeys.actionTemplates.list({ includeInactive }),
    queryFn: () => actionTemplatesApi.list({ includeInactive }),
  });

  const filtered = (query.data ?? []).filter((t) =>
    search.trim().length === 0
      ? true
      : t.name.toLowerCase().includes(search.trim().toLowerCase()) ||
        t.actionType.toLowerCase().includes(search.trim().toLowerCase())
  );

  function openCreate() {
    setEditing(null);
    setFormOpen(true);
  }

  function openEdit(template: ActionTemplateDto) {
    setEditing(template);
    setFormOpen(true);
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between gap-2 border-b border-white/40 px-5 py-4 dark:border-white/10">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold tracking-tight">
            ActionTemplate catalog
          </h2>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Reusable RIOT3 ACT recipes.
          </p>
        </div>
        <Button
          size="sm"
          onClick={openCreate}
          className="rounded-full bg-gradient-to-br from-indigo-500 via-violet-500 to-fuchsia-500 text-white shadow-md shadow-indigo-500/25 hover:shadow-lg hover:shadow-indigo-500/30 hover:brightness-110"
        >
          <Plus className="h-3.5 w-3.5" />
          New
        </Button>
      </div>

      <div className="flex items-center gap-2 border-b border-white/40 px-5 py-3 dark:border-white/10">
        <div className="relative flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Filter by name or type…"
            className="h-9 rounded-full border-white/40 bg-white/55 pl-8 text-sm shadow-inner shadow-white/40 placeholder:text-muted-foreground/70 focus-visible:border-indigo-300/50 focus-visible:ring-indigo-200/40 dark:border-white/10 dark:bg-white/[0.04] dark:shadow-none"
          />
        </div>
        <label className="flex shrink-0 items-center gap-1.5 text-xs text-muted-foreground">
          <Checkbox
            checked={includeInactive}
            onCheckedChange={(v) => setIncludeInactive(v === true)}
          />
          Show inactive
        </label>
      </div>

      <ScrollArea className="flex-1">
        <div className="space-y-2 p-4">
          {query.isLoading ? (
            <div className="flex items-center justify-center py-12 text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" />
            </div>
          ) : query.isError ? (
            <EmptyState
              icon={<Inbox className="h-8 w-8" />}
              title="Couldn't load templates"
              description={(query.error as Error)?.message ?? "Unknown error"}
            />
          ) : filtered.length === 0 ? (
            <EmptyState
              icon={<Inbox className="h-8 w-8" />}
              title={search ? "No matches" : "No templates yet"}
              description={
                search
                  ? "Adjust the filter or clear the search box."
                  : "Click 'New' to create the first one."
              }
            />
          ) : (
            filtered.map((t) => (
              <ActionTemplateRow key={t.id} template={t} onEdit={openEdit} />
            ))
          )}
        </div>
      </ScrollArea>

      <ActionTemplateForm
        open={formOpen}
        onOpenChange={setFormOpen}
        template={editing}
      />
    </div>
  );
}
