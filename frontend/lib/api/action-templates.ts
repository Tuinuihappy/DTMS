// Browser-side fetch helpers + DTO types for the ActionTemplate module.
// Upstream lives behind /api/v1/action-templates and responds with the
// RIOT3 envelope { code, data, message } — we unwrap `data` here so the
// UI never has to think about envelopes.

// DTMS-local STD/ACT category. Distinct from `actionType` below, which is
// the RIOT3 wire string.
export type ActionCategory = "Std" | "Act";

// Backend wire format is uppercase ("STD"/"ACT") via SnakeCaseUpper.
const wireFromCategory = (t: ActionCategory): string => t.toUpperCase();
const categoryFromWire = (s: string): ActionCategory =>
  s === "ACT" ? "Act" : "Std";

export type ActionParameterValueDto = {
  key: string;
  value?: string | number | boolean | null;
};

export type ActionTemplateDto = {
  id: string;
  actionName: string;
  actionCategory: ActionCategory;
  // Literal RIOT3 actionType string (e.g. "standardRobotsCustom") sent to the
  // vendor at dispatch time. Field name matches the RIOT3 wire format
  // exactly so the shape round-trips without renaming.
  actionType: string;
  actionParameters: ActionParameterValueDto[];
  isActive: boolean;
  createdAt: string;
  modifiedAt: string | null;
  createdBy: string | null;
  modifiedBy: string | null;
};

export const DEFAULT_ACTION_TYPE = "standardRobotsCustom";

export type PagedActionTemplates = {
  current: number;
  pages: number;
  size: number;
  total: number;
  records: ActionTemplateDto[];
};

type RiotEnvelope<T> = { code: string; data: T | null; message: string };

const JSON_HEADERS = { "Content-Type": "application/json", Accept: "application/json" };

function idempotencyKey(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return `idk-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

function mutationHeaders(): Record<string, string> {
  return { ...JSON_HEADERS, "Idempotency-Key": idempotencyKey() };
}

async function unwrap<T>(res: Response): Promise<T> {
  // The Next.js proxy already normalises non-2xx into { message } at the
  // same status — surface that string instead of the raw envelope.
  if (!res.ok) {
    let detail = "";
    try {
      const j = await res.json();
      detail = j?.message ?? j?.detail ?? "";
    } catch {
      try {
        detail = await res.text();
      } catch {
        // ignore
      }
    }
    throw new Error(detail || `HTTP ${res.status}`);
  }
  if (res.status === 204) return undefined as T;
  const body = (await res.json()) as RiotEnvelope<T> | T;
  // RIOT3 envelope on success: { code: "0", data, message: "SUCCESS" }.
  if (body && typeof body === "object" && "code" in body && "data" in body) {
    const env = body as RiotEnvelope<T>;
    if (env.code !== "0") throw new Error(env.message || "Unknown error");
    return env.data as T;
  }
  return body as T;
}

function normaliseTemplate(raw: ActionTemplateDto): ActionTemplateDto {
  return {
    ...raw,
    actionCategory:
      typeof raw.actionCategory === "string"
        ? categoryFromWire(raw.actionCategory as unknown as string)
        : raw.actionCategory,
    actionType: raw.actionType ?? DEFAULT_ACTION_TYPE,
    actionParameters: raw.actionParameters ?? [],
  };
}

export type ActionTemplateSortBy =
  | "actionName"
  | "actionCategory"
  | "modifiedAt"
  | "isActive";

export type ListActionTemplatesParams = {
  page?: number;
  size?: number;
  includeInactive?: boolean;
  actionCategory?: ActionCategory;
  search?: string;
  sortBy?: ActionTemplateSortBy;
  sortDir?: "asc" | "desc";
};

export async function listActionTemplates(
  params: ListActionTemplatesParams,
  signal?: AbortSignal,
): Promise<PagedActionTemplates> {
  const qs = new URLSearchParams();
  if (params.page) qs.set("page", String(params.page));
  if (params.size) qs.set("size", String(params.size));
  if (params.includeInactive) qs.set("includeInactive", "true");
  if (params.actionCategory) qs.set("actionCategory", wireFromCategory(params.actionCategory));
  if (params.search && params.search.trim()) qs.set("search", params.search.trim());
  if (params.sortBy) qs.set("sortBy", params.sortBy);
  if (params.sortDir) qs.set("sortDir", params.sortDir);
  const res = await fetch(`/api/action-templates?${qs}`, { cache: "no-store", signal });
  const paged = await unwrap<PagedActionTemplates>(res);
  return {
    ...paged,
    records: (paged.records ?? []).map(normaliseTemplate),
  };
}

export type ActionTemplateStatsDto = {
  total: number;
  active: number;
  inactive: number;
  std: number;
  act: number;
};

export async function getActionTemplateStats(
  signal?: AbortSignal,
): Promise<ActionTemplateStatsDto> {
  const res = await fetch(`/api/action-templates/stats`, { cache: "no-store", signal });
  return unwrap<ActionTemplateStatsDto>(res);
}

export async function getActionTemplate(id: string): Promise<ActionTemplateDto> {
  const res = await fetch(`/api/action-templates/${id}`, { cache: "no-store" });
  return normaliseTemplate(await unwrap<ActionTemplateDto>(res));
}

// RIOT3 takes parameters as a flat array of { key, value } — we collect
// the schema fields (vendor id / param0 / param1 / paramStr) into that
// shape here so the form can present nice labelled inputs.
export type ActionTemplateFormPayload = {
  actionName: string;
  actionCategory: ActionCategory;
  actionType: string;
  vendorActionId: number | null;
  param0: number | null;
  param1: number | null;
  paramStr: string | null;
};

function toParameterArray(p: ActionTemplateFormPayload): ActionParameterValueDto[] {
  const out: ActionParameterValueDto[] = [];
  if (p.vendorActionId != null) out.push({ key: "id", value: p.vendorActionId });
  if (p.param0 != null) out.push({ key: "param0", value: p.param0 });
  if (p.param1 != null) out.push({ key: "param1", value: p.param1 });
  if (p.paramStr != null && p.paramStr.trim() !== "")
    out.push({ key: "param_str", value: p.paramStr });
  return out;
}

// Inverse — used when editing an existing template so the form shows the
// current values in the schema-aware fields. Unknown keys are dropped.
export function formFromTemplate(t: ActionTemplateDto): ActionTemplateFormPayload {
  const find = (k: string) =>
    t.actionParameters.find((p) => p.key.toLowerCase() === k.toLowerCase());
  const num = (k: string): number | null => {
    const v = find(k)?.value;
    if (v == null || v === "") return null;
    const n = typeof v === "number" ? v : Number(v);
    return Number.isFinite(n) ? n : null;
  };
  const str = (k: string): string | null => {
    const v = find(k)?.value;
    if (v == null) return null;
    return String(v);
  };
  return {
    actionName: t.actionName,
    actionCategory: t.actionCategory,
    actionType: t.actionType || DEFAULT_ACTION_TYPE,
    vendorActionId: num("id"),
    param0: num("param0"),
    param1: num("param1"),
    paramStr: str("param_str"),
  };
}

export async function createActionTemplate(
  payload: ActionTemplateFormPayload,
): Promise<ActionTemplateDto> {
  const res = await fetch(`/api/action-templates`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify({
      actionName: payload.actionName,
      actionCategory: wireFromCategory(payload.actionCategory),
      actionType: payload.actionType || DEFAULT_ACTION_TYPE,
      actionParameters: toParameterArray(payload),
    }),
  });
  return normaliseTemplate(await unwrap<ActionTemplateDto>(res));
}

export async function updateActionTemplate(
  id: string,
  payload: ActionTemplateFormPayload,
): Promise<void> {
  const res = await fetch(`/api/action-templates/${id}`, {
    method: "PUT",
    headers: mutationHeaders(),
    body: JSON.stringify({
      actionName: payload.actionName,
      actionCategory: wireFromCategory(payload.actionCategory),
      actionType: payload.actionType || DEFAULT_ACTION_TYPE,
      actionParameters: toParameterArray(payload),
    }),
  });
  await unwrap(res);
}

export async function deleteActionTemplate(id: string): Promise<void> {
  const res = await fetch(`/api/action-templates/${id}`, {
    method: "DELETE",
    headers: mutationHeaders(),
  });
  await unwrap(res);
}

export async function activateActionTemplate(id: string): Promise<void> {
  const res = await fetch(`/api/action-templates/${id}/activate`, {
    method: "POST",
    headers: mutationHeaders(),
  });
  await unwrap(res);
}

export async function deactivateActionTemplate(id: string): Promise<void> {
  const res = await fetch(`/api/action-templates/${id}/deactivate`, {
    method: "POST",
    headers: mutationHeaders(),
  });
  await unwrap(res);
}

// Dropdown-friendly view of an active action template. The mission's
// `actionTemplateName` is the contract field, so we expose that as the
// value while showing actionType + category as secondary context.
export type ActionTemplateOption = {
  name: string;
  category: ActionCategory;
  actionType: string;
};

// One-shot fetch for the editor combobox — paginates once with a
// high page size and returns active templates only. If a tenant
// ever ships >500 active templates we'll need a search-as-you-type
// flow, but for now client-side filter on the loaded list is fine.
export async function getActionTemplateOptions(
  signal?: AbortSignal,
): Promise<ActionTemplateOption[]> {
  const paged = await listActionTemplates(
    { page: 1, size: 500, includeInactive: false, sortBy: "actionName", sortDir: "asc" },
    signal,
  );
  return paged.records
    .filter((t) => t.isActive && t.actionName)
    .map((t) => ({ name: t.actionName, category: t.actionCategory, actionType: t.actionType }));
}

// React hook — fetches the dropdown list once on mount.
import { useEffect, useState } from "react";

export type UseActionTemplateOptionsResult = {
  templates: ActionTemplateOption[];
  loading: boolean;
  error: string | null;
};

export function useActionTemplateOptions(): UseActionTemplateOptionsResult {
  const [templates, setTemplates] = useState<ActionTemplateOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    let cancelled = false;
    setLoading(true);
    setError(null);
    getActionTemplateOptions(ctrl.signal)
      .then((opts) => {
        if (!cancelled) setTemplates(opts);
      })
      .catch((e: Error) => {
        if (!cancelled && e.name !== "AbortError")
          setError(e.message || "Failed to load action templates");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
      ctrl.abort();
    };
  }, []);

  return { templates, loading, error };
}
