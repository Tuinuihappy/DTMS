import { fetchJson } from "@/lib/api";
import type {
  CreateDraftDeliveryOrderRequest,
  DeliveryOrderDetailDto,
  DeliveryOrderListDto,
  ItemDto,
  ItemStatus,
  LifecycleResult,
  OrderStatus,
  PagedResult,
} from "@/types/delivery-order";

const ROOT = "/api/v1/delivery-orders";

// Every mutation gets a fresh idempotency key. crypto.randomUUID is
// available in all evergreen browsers + Node 24 — no fallback needed.
function withIdempotencyKey(extra?: HeadersInit): HeadersInit {
  return {
    ...(extra ?? {}),
    "Idempotency-Key": crypto.randomUUID(),
  };
}

export interface ListDeliveryOrdersParams {
  status?: OrderStatus;
  page?: number;
  pageSize?: number;
}

function buildListQuery(params: ListDeliveryOrdersParams): string {
  const qs = new URLSearchParams();
  if (params.status) qs.set("status", params.status);
  if (params.page) qs.set("page", String(params.page));
  if (params.pageSize) qs.set("pageSize", String(params.pageSize));
  const s = qs.toString();
  return s ? `?${s}` : "";
}

export const deliveryOrdersApi = {
  list(params: ListDeliveryOrdersParams = {}) {
    return fetchJson<PagedResult<DeliveryOrderListDto>>(
      `${ROOT}${buildListQuery(params)}`
    );
  },

  get(id: string) {
    return fetchJson<DeliveryOrderDetailDto>(`${ROOT}/${id}`);
  },

  items(id: string, status?: ItemStatus) {
    const qs = status ? `?status=${encodeURIComponent(status)}` : "";
    return fetchJson<ItemDto[]>(`${ROOT}/${id}/items${qs}`);
  },

  create(body: CreateDraftDeliveryOrderRequest) {
    return fetchJson<DeliveryOrderDetailDto>(ROOT, {
      method: "POST",
      headers: withIdempotencyKey(),
      body: JSON.stringify(body),
    });
  },

  submit(id: string) {
    return fetchJson<LifecycleResult>(`${ROOT}/${id}/submit`, {
      method: "POST",
      headers: withIdempotencyKey(),
      body: JSON.stringify({ orderId: id }),
    });
  },

  confirm(id: string, confirmedBy?: string) {
    return fetchJson<LifecycleResult>(`${ROOT}/${id}/confirm`, {
      method: "POST",
      headers: withIdempotencyKey(),
      body: JSON.stringify(confirmedBy ? { confirmedBy } : {}),
    });
  },

  reject(id: string, reason: string, rejectedBy?: string) {
    return fetchJson<void>(`${ROOT}/${id}/reject`, {
      method: "POST",
      headers: withIdempotencyKey(),
      body: JSON.stringify(rejectedBy ? { reason, rejectedBy } : { reason }),
    });
  },

  hold(id: string, reason: string, heldBy?: string) {
    return fetchJson<void>(`${ROOT}/${id}/hold`, {
      method: "POST",
      headers: withIdempotencyKey(),
      body: JSON.stringify(heldBy ? { reason, heldBy } : { reason }),
    });
  },

  release(id: string, releasedBy?: string) {
    return fetchJson<LifecycleResult>(`${ROOT}/${id}/release`, {
      method: "POST",
      headers: withIdempotencyKey(),
      body: JSON.stringify(releasedBy ? { releasedBy } : {}),
    });
  },

  cancel(id: string, reason: string) {
    return fetchJson<void>(`${ROOT}/${id}`, {
      method: "DELETE",
      headers: withIdempotencyKey(),
      body: JSON.stringify({ reason }),
    });
  },
};
