import { fetchJson } from "@/lib/api";
import type {
  CreateOrderTemplateRequest,
  InstantiateOrderTemplateRequest,
  OrderTemplateDto,
  UpdateOrderTemplateRequest,
} from "@/types/order-template";

const ROOT = "/api/v1/planning/order-templates";

export interface ListOrderTemplatesParams {
  includeInactive?: boolean;
}

function buildQuery(params: ListOrderTemplatesParams): string {
  if (!params.includeInactive) return "";
  return "?includeInactive=true";
}

// Shape returned by /instantiate. With dryRun=true the backend returns
// the resolved envelope without calling RIOT3; with dryRun=false it
// forwards to RIOT3 and tacks the vendor's orderKey onto the same shape.
export interface InstantiateResult {
  upperKey: string;
  resolvedEnvelope: unknown;
  riotOrderKey?: string | null;
  dryRun: boolean;
}

export const orderTemplatesApi = {
  list(params: ListOrderTemplatesParams = {}) {
    return fetchJson<OrderTemplateDto[]>(`${ROOT}${buildQuery(params)}`);
  },

  get(id: string) {
    return fetchJson<OrderTemplateDto>(`${ROOT}/${id}`);
  },

  create(body: CreateOrderTemplateRequest) {
    return fetchJson<string>(ROOT, {
      method: "POST",
      body: JSON.stringify(body),
    });
  },

  update(id: string, body: UpdateOrderTemplateRequest) {
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

  instantiate(id: string, body: InstantiateOrderTemplateRequest) {
    return fetchJson<InstantiateResult>(`${ROOT}/${id}/instantiate`, {
      method: "POST",
      body: JSON.stringify(body),
    });
  },
};
