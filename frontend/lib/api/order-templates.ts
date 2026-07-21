// Browser-side fetch helpers + DTO types for the OrderTemplate module.
// Upstream lives behind /api/v1/order-templates and responds with the
// RIOT3 envelope { code, data, message } — we unwrap `data` here so the
// UI never has to think about envelopes.

export type MissionType = "MOVE" | "ACT";
export type StructureType = "sequence" | "parallel";

export type MissionParameterDto = {
  key: string;
  value: string | number | boolean | null;
};

export type OrderTemplateMissionDto = {
  sequence: number;
  type: MissionType;
  category: string;
  mapId: number | null;
  stationId: number | null;
  actionType: string | null;
  blockingType: string | null;
  actionParameters: MissionParameterDto[] | null;
  actionTemplateName: string | null;
};

export type TransportOrderDto = {
  structureType: StructureType | string;
  priority: number;
  missions: OrderTemplateMissionDto[];
};

export type OrderTemplateDto = {
  id: string;
  name: string;
  priority: number;
  transportOrder: TransportOrderDto;
  appointVehicleKey: string | null;
  appointVehicleName: string | null;
  appointVehicleGroupKey: string | null;
  appointVehicleGroupName: string | null;
  appointQueueWaitArea: string | null;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  modifiedAt: string | null;
  createdBy: string | null;
  modifiedBy: string | null;
  pickupStationId: string | null;
  dropStationId: string | null;
};

export type PagedOrderTemplates = {
  current: number;
  pages: number;
  size: number;
  total: number;
  records: OrderTemplateDto[];
};

type RiotEnvelope<T> = { code: string; data: T | null; message: string };

const JSON_HEADERS = { "Content-Type": "application/json", Accept: "application/json" };

// crypto.randomUUID only exists in a secure context, so it is missing when the
// app is opened over plain http on a LAN IP (how tablets reach it here).
// Falling back keeps dispatch working instead of throwing.
export function idempotencyKey(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return `idk-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

function mutationHeaders(): Record<string, string> {
  return { ...JSON_HEADERS, "Idempotency-Key": idempotencyKey() };
}

async function unwrap<T>(res: Response): Promise<T> {
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
  if (body && typeof body === "object" && "code" in body && "data" in body) {
    const env = body as RiotEnvelope<T>;
    if (env.code !== "0") throw new Error(env.message || "Unknown error");
    return env.data as T;
  }
  return body as T;
}

export type OrderTemplateSortKey =
  | "name"
  | "priority"
  | "modifiedAt"
  | "createdAt"
  | "isActive";

export type ListOrderTemplatesParams = {
  page?: number;
  size?: number;
  includeInactive?: boolean;
  sortBy?: OrderTemplateSortKey;
  sortDir?: "asc" | "desc";
};

export async function listOrderTemplates(
  params: ListOrderTemplatesParams,
  signal?: AbortSignal,
): Promise<PagedOrderTemplates> {
  const qs = new URLSearchParams();
  if (params.page) qs.set("page", String(params.page));
  if (params.size) qs.set("size", String(params.size));
  if (params.includeInactive) qs.set("includeInactive", "true");
  if (params.sortBy) qs.set("sortBy", params.sortBy);
  if (params.sortDir) qs.set("sortDir", params.sortDir);
  const res = await fetch(`/api/order-templates?${qs}`, { cache: "no-store", signal });
  return unwrap<PagedOrderTemplates>(res);
}

export async function getOrderTemplate(id: string): Promise<OrderTemplateDto> {
  const res = await fetch(`/api/order-templates/${id}`, { cache: "no-store" });
  return unwrap<OrderTemplateDto>(res);
}

// ── Mutation payloads (match RIOT3 wire shape) ──────────────────────────

export type MissionPayload = {
  type: MissionType;
  category?: string | null;
  mapId?: number | null;
  stationId?: number | null;
  actionType?: string | null;
  blockingType?: string | null;
  actionParameters?: MissionParameterDto[] | null;
  actionTemplateName?: string | null;
};

export type TransportOrderPayload = {
  structureType?: StructureType | null;
  priority?: number | null;
  missions: MissionPayload[];
};

export type CreateOrderTemplatePayload = {
  name: string;
  priority: number;
  transportOrder: TransportOrderPayload;
  appointVehicleKey?: string | null;
  appointVehicleName?: string | null;
  appointVehicleGroupKey?: string | null;
  appointVehicleGroupName?: string | null;
  appointQueueWaitArea?: string | null;
  description?: string | null;
  pickupStationCode?: string | null;
  dropStationCode?: string | null;
};

export type UpdateOrderTemplatePayload = Omit<CreateOrderTemplatePayload, "name">;

export async function createOrderTemplate(
  payload: CreateOrderTemplatePayload,
): Promise<OrderTemplateDto> {
  const res = await fetch(`/api/order-templates`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(payload),
  });
  return unwrap<OrderTemplateDto>(res);
}

export async function updateOrderTemplate(
  id: string,
  payload: UpdateOrderTemplatePayload,
): Promise<void> {
  const res = await fetch(`/api/order-templates/${id}`, {
    method: "PUT",
    headers: mutationHeaders(),
    body: JSON.stringify(payload),
  });
  await unwrap(res);
}

export async function deleteOrderTemplate(id: string): Promise<void> {
  const res = await fetch(`/api/order-templates/${id}`, {
    method: "DELETE",
    headers: mutationHeaders(),
  });
  await unwrap(res);
}

export async function activateOrderTemplate(id: string): Promise<void> {
  const res = await fetch(`/api/order-templates/${id}/activate`, {
    method: "POST",
    headers: mutationHeaders(),
  });
  await unwrap(res);
}

export async function deactivateOrderTemplate(id: string): Promise<void> {
  const res = await fetch(`/api/order-templates/${id}/deactivate`, {
    method: "POST",
    headers: mutationHeaders(),
  });
  await unwrap(res);
}

// ── Create-from-template (POST /{id}/create) ────────────────────────────
// Resolves ActionTemplate references against the catalog and POSTs the
// full envelope to RIOT3. dryRun=true skips the vendor call so operators
// can preview the resolved missions before committing.

export type CreateFromTemplatePayload = {
  priority?: number | null;
  appointVehicleKey?: string | null;
  appointVehicleName?: string | null;
  appointVehicleGroupKey?: string | null;
  appointVehicleGroupName?: string | null;
  appointQueueWaitArea?: string | null;
  upperKey?: string | null;
  dryRun?: boolean;
};

export type ResolvedActionParameter = {
  key: string;
  value: string | number | boolean | null;
};

export type ResolvedMission = {
  type: MissionType | string;
  category?: string | null;
  mapId?: number | null;
  stationId?: number | null;
  actionType?: string | null;
  blockingType?: string | null;
  actionParameters?: ResolvedActionParameter[] | null;
};

// Flat, matching the backend `ResolvedOrder` record — structureType and
// missions sit at the top level, NOT nested under a transportOrder object
// (that nesting is the stored-template wire shape, a different thing).
export type ResolvedOrder = {
  name?: string | null;
  priority?: number | null;
  structureType?: string | null;
  transportOrderPriority?: number | null;
  missions?: ResolvedMission[] | null;
  appointVehicleKey?: string | null;
  appointVehicleName?: string | null;
  appointVehicleGroupKey?: string | null;
  appointVehicleGroupName?: string | null;
  appointQueueWaitArea?: string | null;
};

export type CreateFromTemplateResult = {
  upperKey: string;
  riot3OrderKey?: string | null;
  resolvedOrder: ResolvedOrder;
  dryRun: boolean;
  // True when the backend returned the stored outcome of an earlier identical
  // request instead of dispatching again.
  replayed?: boolean;
};

// Wire code for "an identical dispatch is still being confirmed". Not a
// failure — the UI shows it as a state so the operator waits and checks
// rather than firing a second robot order.
export const DISPATCH_IN_PROGRESS = "DISPATCH_IN_PROGRESS";

export class DispatchInProgressError extends Error {
  readonly code = DISPATCH_IN_PROGRESS;
}

// `idempotencyKey` identifies the operator's INTENT, not the HTTP request.
// The caller owns its lifetime: same key while one dispatch is being retried,
// a fresh key for a genuinely new dispatch. Repeated dispatch of the same
// template is normal, so a new key must always go straight through.
export async function createOrderFromTemplate(
  id: string,
  payload: CreateFromTemplatePayload,
  opts?: { idempotencyKey?: string; signal?: AbortSignal },
): Promise<CreateFromTemplateResult> {
  const headers: Record<string, string> = { ...JSON_HEADERS };
  if (opts?.idempotencyKey) headers["Idempotency-Key"] = opts.idempotencyKey;

  const res = await fetch(`/api/order-templates/${id}/create`, {
    method: "POST",
    headers,
    body: JSON.stringify(payload ?? {}),
    signal: opts?.signal,
  });

  if (res.status === 409) {
    const body = await res.json().catch(() => null);
    throw new DispatchInProgressError(
      body?.message ?? "A dispatch with this key is still being confirmed.",
    );
  }
  return unwrap<CreateFromTemplateResult>(res);
}

export type LastDispatchDto = {
  status: "InProgress" | "Succeeded" | "Failed" | string;
  upperKey: string;
  vendorOrderKey?: string | null;
  createdAt: string;
  completedAt?: string | null;
};

// Informational only — shown so an operator unsure whether their last click
// landed can look instead of dispatching again. Never gates the dispatch.
export async function getLastDispatch(
  id: string,
  signal?: AbortSignal,
): Promise<LastDispatchDto | null> {
  const res = await fetch(`/api/order-templates/${id}/last-dispatch`, {
    cache: "no-store",
    signal,
  });
  return unwrap<LastDispatchDto | null>(res);
}

// ── Derived stats (computed client-side from the list) ─────────────────
// Backend doesn't expose a /stats endpoint for OrderTemplate yet, so the
// KPI strip derives counters from the fetched records. Templates catalog
// is small (clamped at 200 in the handler) so this is cheap.

export type OrderTemplateStats = {
  total: number;
  active: number;
  inactive: number;
  avgMissions: number;
  withVehicleBinding: number;
};

export function deriveStats(records: OrderTemplateDto[]): OrderTemplateStats {
  const total = records.length;
  const active = records.filter((r) => r.isActive).length;
  const totalMissions = records.reduce(
    (acc, r) => acc + (r.transportOrder?.missions?.length ?? 0),
    0,
  );
  const withBinding = records.filter(
    (r) =>
      !!(
        r.appointVehicleKey ||
        r.appointVehicleName ||
        r.appointVehicleGroupKey ||
        r.appointVehicleGroupName ||
        r.appointQueueWaitArea
      ),
  ).length;
  return {
    total,
    active,
    inactive: total - active,
    avgMissions: total === 0 ? 0 : Math.round((totalMissions / total) * 10) / 10,
    withVehicleBinding: withBinding,
  };
}
