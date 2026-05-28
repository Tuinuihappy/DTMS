import { fetchJson } from "@/lib/api";
import type {
  ActionTemplateDto,
  CreateActionTemplateRequest,
  UpdateActionTemplateRequest,
} from "@/types/action-template";

const ROOT = "/api/v1/planning/action-templates";

export interface ListActionTemplatesParams {
  includeInactive?: boolean;
  actionType?: string;
}

function buildQuery(params: ListActionTemplatesParams): string {
  const qs = new URLSearchParams();
  if (params.includeInactive) qs.set("includeInactive", "true");
  if (params.actionType) qs.set("actionType", params.actionType);
  const s = qs.toString();
  return s ? `?${s}` : "";
}

export const actionTemplatesApi = {
  list(params: ListActionTemplatesParams = {}) {
    return fetchJson<ActionTemplateDto[]>(`${ROOT}${buildQuery(params)}`);
  },

  get(id: string) {
    return fetchJson<ActionTemplateDto>(`${ROOT}/${id}`);
  },

  create(body: CreateActionTemplateRequest) {
    return fetchJson<string>(ROOT, {
      method: "POST",
      body: JSON.stringify(body),
    });
  },

  update(id: string, body: UpdateActionTemplateRequest) {
    return fetchJson<void>(`${ROOT}/${id}`, {
      method: "PATCH",
      body: JSON.stringify(body),
    });
  },

  activate(id: string) {
    return fetchJson<void>(`${ROOT}/${id}/activate`, { method: "POST" });
  },

  deactivate(id: string) {
    return fetchJson<void>(`${ROOT}/${id}/deactivate`, { method: "POST" });
  },

  delete(id: string) {
    return fetchJson<void>(`${ROOT}/${id}`, { method: "DELETE" });
  },
};
