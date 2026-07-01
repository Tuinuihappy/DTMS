// Shared DTO types + browser-side fetchers for the delivery-orders feature.
// The actual upstream call to the .NET API happens inside server-side
// Next.js route handlers under app/api/delivery-orders/* — these helpers
// are what client components call.

export type OrderStatus =
  | "Draft"
  | "Submitted"
  | "Validated"
  | "Confirmed"
  | "Planning"
  | "Planned"
  | "Dispatched"
  | "InProgress"
  | "Completed"
  | "PartiallyCompleted"
  | "Held"
  | "Failed"
  | "Amended"
  | "Cancelled"
  | "Rejected";

export type Priority = "Low" | "Normal" | "High" | "Critical";
// Phase P5 — broadened from a closed union to plain string. Admins can
// register new SystemClients (wms-acme, tms-bravo, ...) without a FE
// deploy; the API stamps the lowercase iam.SystemClients.Key slug and
// the UI renders it as-is (or via sourceSystemDisplayName when present).
export type SourceSystem = string;
export type TransportMode = "Amr" | "Manual" | "Fleet";
export type Uom = "KG" | "G" | "LB" | "EA" | "BOX" | "PALLET" | "CASE";
export type HandlingInstruction =
  | "Fragile"
  | "ThisSideUp"
  | "DoNotStack"
  | "HeavyLift"
  | "Sharp"
  | "KeepDry"
  | "KeepDark"
  | "PinchHazard";

export type ServiceWindowDto = { earliestUtc: string | null; latestUtc: string | null };

export type DeliveryOrderListDto = {
  id: string;
  orderRef: string;
  sourceSystem: SourceSystem;
  // Phase P5 snapshot — captured at create-time; nullable for legacy rows.
  sourceSystemDisplayName: string | null;
  priority: Priority;
  orderStatus: OrderStatus;
  serviceWindow: ServiceWindowDto | null;
  submittedAt: string | null;
  createdBy: string | null;
  requestedBy: string | null;
  notes: string | null;
  createdDate: string;
  updatedDate: string | null;
  totalWeightKg: number;
  totalQuantity: number;
  totalItems: number;
  requestedTransportMode: TransportMode | null;
  // Tri-state: true = drop POD scan required, false = always auto-deliver,
  // null = fall back to the route's OrderTemplate.RequiresDropPod.
  requiresDropPod: boolean | null;
  // Tri-state (default false at the API): true = operator must scan at
  // pickup for audit, null = fall back to OrderTemplate.RequiresPickupPod.
  // Audit-only — never blocks the vendor flow.
  requiresPickupPod: boolean | null;
};

export type ItemDto = {
  id: string;
  itemSeq: number;
  itemId: string;
  description: string | null;
  pickupLocationCode: string;
  dropLocationCode: string;
  loadUnitProfileCode: string | null;
  dimensions: {
    lengthMm: number;
    widthMm: number;
    heightMm: number;
    volumeCBM: number;
  } | null;
  weightKg: number | null;
  quantity: { value: number; uom: Uom };
  hazmat: { classCode: string; packingGroup: PackingGroup | null } | null;
  temperature: { minC: number | null; maxC: number | null } | null;
  handlingInstructions: HandlingInstruction[];
  status: ItemStatus;
  tripId: string | null;
  attemptNumber: number | null;
  droppedOffAt: string | null;
  pickupPod: PodEventDto | null;
  dropPod: PodEventDto | null;
};

export type PodEventDto = {
  scannedAt: string;
  scannedBy: string;
  method: string;
  reference: string | null;
};

export type PodScanType = "Pickup" | "Drop";

export type ItemStatus =
  | "Pending"
  | "Picked"
  | "DroppedOff"
  | "Delivered"
  | "Failed"
  | "Returned"
  | "Cancelled";

export type PodMethod = "Barcode" | "Manual" | "Signature" | "Confirm";

export async function confirmItemPod(
  orderId: string,
  itemId: string,
  body: {
    scannedBy: string;
    method: PodMethod;
    reference?: string | null;
    scanType?: PodScanType;   // defaults to "Drop" server-side
  },
): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${orderId}/items/${itemId}/pod-scan`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Idempotency-Key": crypto.randomUUID(),
    },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `POD scan failed (${res.status})`);
  }
}

export type DeliveryOrderDetailDto = DeliveryOrderListDto & {
  items: ItemDto[];
};

export type PagedResult<T> = {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
};

// The .NET API serializes enums with JsonNamingPolicy.SnakeCaseUpper
// (e.g. "IN_PROGRESS", "AMR", "PARTIALLY_COMPLETED") and wraps lists as
// { data, totalCount, page, pageSize }. Convert at the network boundary
// so the UI always speaks PascalCase + `items` regardless of upstream.
function pascalFromUpperSnake(s: string): string {
  if (!s) return s;
  return s
    .toLowerCase()
    .split("_")
    .map((p) => (p.length ? p[0].toUpperCase() + p.slice(1) : p))
    .join("");
}

function upperSnakeFromPascal(s: string): string {
  if (!s) return s;
  return s
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1_$2")
    .toUpperCase();
}

// Phase P5 — sourceSystem is intentionally NOT in this list: it's now a
// raw lowercase slug (from iam.SystemClients.Key) with no PascalCase
// normalization needed. The other fields remain closed enums.
const ENUM_FIELDS: Array<keyof DeliveryOrderListDto | keyof ItemDto> = [
  "priority",
  "orderStatus",
  "requestedTransportMode",
  "status",
];

function normalizeOrder<T extends Record<string, unknown>>(o: T): T {
  const next = { ...o } as Record<string, unknown>;
  for (const k of ENUM_FIELDS) {
    const v = next[k as string];
    if (typeof v === "string") next[k as string] = pascalFromUpperSnake(v);
  }
  // Items inside detail dto
  if (Array.isArray(next.items)) {
    next.items = (next.items as Record<string, unknown>[]).map((it) => {
      const nit = { ...it };
      if (typeof nit.status === "string")
        nit.status = pascalFromUpperSnake(nit.status as string);
      if (nit.quantity && typeof nit.quantity === "object") {
        const q = nit.quantity as Record<string, unknown>;
        if (typeof q.uom === "string") q.uom = pascalFromUpperSnake(q.uom as string).toUpperCase();
      }
      if (Array.isArray(nit.handlingInstructions)) {
        nit.handlingInstructions = (nit.handlingInstructions as string[]).map((h) =>
          pascalFromUpperSnake(h),
        );
      }
      return nit;
    });
  }
  return next as T;
}

export type PackingGroup = "I" | "II" | "III";

// Phase P4/P5 — requestedBy is now server-side (JWT name), not client-
// supplied. The field was removed from CreateDraftDeliveryOrderCommand.
export type CreateOrderPayload = {
  orderRef: string;
  priority: Priority;
  notes?: string;
  requestedTransportMode?: TransportMode;
  requiresDropPod?: boolean | null;
  requiresPickupPod?: boolean | null;
  serviceWindow: { earliestUtc?: string; latestUtc?: string };
  items: Array<{
    itemId: string;
    description?: string;
    pickupLocationCode: string;
    dropLocationCode: string;
    loadUnitProfileCode?: string;
    dimensions?: { lengthMm: number; widthMm: number; heightMm: number };
    weightKg?: number;
    quantity: { value: number; uom: Uom };
    hazmat?: { classCode: string; packingGroup?: PackingGroup };
    temperature?: { minC?: number; maxC?: number };
    handlingInstructions?: HandlingInstruction[];
  }>;
};

const JSON_HEADERS = { "Content-Type": "application/json", Accept: "application/json" };

// Idempotency-Key — one fresh UUID per mutation call. The backend caches
// the response keyed by (key + method + path + args hash); a network
// retry with the same key replays the cached result instead of double-
// submitting. Browsers expose crypto.randomUUID on every modern engine
// we ship to; fall back to a Math.random-derived id for older runtimes
// just to keep the contract non-null.
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
  return (await res.json()) as T;
}

// Upstream PagedResult uses `data` as the list key; map to `items` for the UI.
type UpstreamPagedResult<T> = {
  data?: T[];
  items?: T[];
  totalCount: number;
  page: number;
  pageSize: number;
};

function denormalizeCreate(payload: CreateOrderPayload): unknown {
  return {
    ...payload,
    priority: upperSnakeFromPascal(payload.priority),
    requestedTransportMode: payload.requestedTransportMode
      ? upperSnakeFromPascal(payload.requestedTransportMode)
      : undefined,
    items: payload.items.map((it) => ({
      ...it,
      quantity: { value: it.quantity.value, uom: it.quantity.uom },
      handlingInstructions: it.handlingInstructions?.map((h) => upperSnakeFromPascal(h)),
    })),
  };
}

export type StatusBucketParam = "active" | "completed" | "terminal";

export type ListOrdersParams = {
  status?: OrderStatus;
  bucket?: StatusBucketParam;
  priority?: Priority;
  transportMode?: TransportMode;
  search?: string;
  /** Phase P4 — derived projection filters. */
  hasFailedTrip?: boolean;
  hasActiveJob?: boolean;
  sortBy?: "createdDate" | "orderRef" | "priority" | "status" | "totalWeightKg" | "updatedAt";
  sortDir?: "asc" | "desc";
  /**
   * Inclusive [createdFromUtc, createdToUtc] window applied to the
   * server-side CreatedAt column. Either side may be omitted to leave
   * that bound open. Send ISO-8601 UTC strings (the `Z` suffix the
   * backend already speaks for every other timestamp).
   */
  createdFromUtc?: string;
  createdToUtc?: string;
  page?: number;
  pageSize?: number;
};

export async function listOrders(
  params: ListOrdersParams,
  signal?: AbortSignal,
): Promise<PagedResult<DeliveryOrderListDto>> {
  const qs = new URLSearchParams();
  if (params.status) qs.set("status", upperSnakeFromPascal(params.status));
  if (params.bucket) qs.set("statusBucket", params.bucket);
  if (params.priority) qs.set("priority", upperSnakeFromPascal(params.priority));
  if (params.transportMode)
    qs.set("transportMode", upperSnakeFromPascal(params.transportMode));
  if (params.search) qs.set("search", params.search);
  if (params.hasFailedTrip != null) qs.set("hasFailedTrip", String(params.hasFailedTrip));
  if (params.hasActiveJob != null) qs.set("hasActiveJob", String(params.hasActiveJob));
  if (params.sortBy) qs.set("sortBy", params.sortBy);
  if (params.sortDir) qs.set("sortDir", params.sortDir);
  if (params.createdFromUtc) qs.set("createdFromUtc", params.createdFromUtc);
  if (params.createdToUtc) qs.set("createdToUtc", params.createdToUtc);
  if (params.page) qs.set("page", String(params.page));
  if (params.pageSize) qs.set("pageSize", String(params.pageSize));
  const res = await fetch(`/api/delivery-orders?${qs}`, { cache: "no-store", signal });
  const raw = await unwrap<UpstreamPagedResult<DeliveryOrderListDto>>(res);
  const list = raw.data ?? raw.items ?? [];
  return {
    items: list.map(normalizeOrder),
    totalCount: raw.totalCount,
    page: raw.page,
    pageSize: raw.pageSize,
  };
}

export type OrderStats = {
  total: number;
  active: number;
  completed: number;
  totalWeightKg: number;
  byStatus: Record<OrderStatus, number>;
};

export async function getOrderStats(signal?: AbortSignal): Promise<OrderStats> {
  const res = await fetch(`/api/delivery-orders/stats`, { cache: "no-store", signal });
  const raw = await unwrap<{
    total: number;
    active: number;
    completed: number;
    totalWeightKg: number;
    byStatus: Record<string, number>;
  }>(res);
  // Backend keys are SnakeCaseUpper enum names ("DRAFT", "IN_PROGRESS") —
  // re-key into PascalCase to match the OrderStatus union the UI uses.
  const byStatus: Partial<Record<OrderStatus, number>> = {};
  for (const [k, v] of Object.entries(raw.byStatus)) {
    byStatus[pascalFromUpperSnake(k) as OrderStatus] = v;
  }
  return {
    total: raw.total,
    active: raw.active,
    completed: raw.completed,
    totalWeightKg: raw.totalWeightKg,
    byStatus: byStatus as Record<OrderStatus, number>,
  };
}

export async function getOrder(id: string): Promise<DeliveryOrderDetailDto> {
  const res = await fetch(`/api/delivery-orders/${id}`, { cache: "no-store" });
  const raw = await unwrap<DeliveryOrderDetailDto>(res);
  return normalizeOrder(raw);
}

export type TimelineEntryDto = {
  id: string;
  eventType: string;
  details: string | null;
  actorId: string | null;
  occurredAt: string;
};

export async function getOrderTimeline(id: string): Promise<TimelineEntryDto[]> {
  const res = await fetch(`/api/delivery-orders/${id}/timeline`, {
    cache: "no-store",
  });
  return unwrap<TimelineEntryDto[]>(res);
}

// ── Full audit (Phase 4.2) ──────────────────────────────────────────────

export type FullAuditEntryDto = {
  id: string;
  source: "Order" | "Amendment" | "TripExecution" | "TripRetry";
  eventType: string;
  details: string | null;
  actorId: string | null;
  occurredAt: string;
  relatedTripId: string | null;
  attemptNumber: number | null;
  // S.1 follow-up — populated from ActorContext at write time.
  // Null on rows projected from pre-1.2 events (backfilled history).
  channel: string | null;
  displayName: string | null;
};

export type FullOrderAuditDto = {
  orderId: string;
  totalEntries: number;
  entries: FullAuditEntryDto[];
};

export async function getFullOrderAudit(id: string): Promise<FullOrderAuditDto> {
  const res = await fetch(`/api/delivery-orders/${id}/audit-full`, {
    cache: "no-store",
  });
  return unwrap<FullOrderAuditDto>(res);
}

export async function createOrder(payload: CreateOrderPayload): Promise<{ id: string }> {
  const res = await fetch(`/api/delivery-orders`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(denormalizeCreate(payload)),
  });
  return unwrap<{ id: string }>(res);
}

// Update a draft order. Backend accepts the same payload shape as
// create (minus the id which goes through the URL). Only allowed when
// the order is still in Draft — backend rejects otherwise.
export async function updateOrder(
  id: string,
  payload: CreateOrderPayload,
): Promise<DeliveryOrderDetailDto> {
  const res = await fetch(`/api/delivery-orders/${id}`, {
    method: "PUT",
    headers: mutationHeaders(),
    body: JSON.stringify(denormalizeCreate(payload)),
  });
  const raw = await unwrap<DeliveryOrderDetailDto>(res);
  return normalizeOrder(raw);
}

export async function deleteOrder(id: string, reason: string): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${id}`, {
    method: "DELETE",
    headers: mutationHeaders(),
    body: JSON.stringify({ reason }),
  });
  if (!res.ok && res.status !== 204) await unwrap(res);
}

// Backend Phase 2 — bulk cancel many orders in one POST. Backend
// returns 200 on full success, 207 Multi-Status when some rows
// failed, 400 if every id failed (or the request was invalid).
// Caller handles both 200/207 the same way; result.failures may be
// empty on full success.
export type BulkCancelOrdersResult = {
  succeeded: string[];
  failures: { orderId: string; reason: string }[];
};

export async function bulkCancelOrders(
  orderIds: string[],
  reason: string,
): Promise<BulkCancelOrdersResult> {
  const res = await fetch(`/api/delivery-orders/bulk-cancel`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify({ orderIds, reason }),
  });
  if (res.status !== 200 && res.status !== 207) {
    return await unwrap<BulkCancelOrdersResult>(res);
  }
  return (await res.json()) as BulkCancelOrdersResult;
}

// Phase P5 — submit auto-confirms atomically on the backend (Draft →
// Confirmed in one command). The response carries the confirmed order
// plus any weight-warning quality issues, mirroring the system path's
// UpstreamOrderAckDto. Callers can update local state directly from the
// returned order instead of firing a follow-up GET.
export type SubmitOrderResult = {
  order: DeliveryOrderDetailDto;
  warnings: Array<{ code: string; message: string }>;
};

export async function submitOrder(id: string): Promise<SubmitOrderResult> {
  const res = await fetch(`/api/delivery-orders/${id}/submit`, {
    method: "POST",
    headers: mutationHeaders(),
  });
  if (!res.ok) await unwrap(res);
  const raw = (await res.json()) as SubmitOrderResult;
  // Backend serializes enums as UPPER_SNAKE (NORMAL, CONFIRMED, ...) but
  // the UI's badge maps + type unions expect PascalCase (Normal,
  // Confirmed). Run the same normalizer other endpoints use so the
  // returned order can be spread into list/detail state directly.
  return { ...raw, order: normalizeOrder(raw.order) };
}

export async function rejectOrder(
  id: string,
  body: { reason: string; rejectedBy?: string },
): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${id}/reject`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(body),
  });
  if (!res.ok && res.status !== 204) await unwrap(res);
}

export async function holdOrder(
  id: string,
  body: { reason: string; heldBy?: string },
): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${id}/hold`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(body),
  });
  if (!res.ok && res.status !== 204) await unwrap(res);
}

export async function releaseOrder(
  id: string,
  body: { releasedBy?: string } = {},
): Promise<{ orderId: string; warnings?: string[] } | void> {
  const res = await fetch(`/api/delivery-orders/${id}/release`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(body),
  });
  if (res.status === 204) return;
  return unwrap<{ orderId: string; warnings?: string[] }>(res);
}

export async function reopenOrder(
  id: string,
  body: { reopenedBy: string; reason: string },
): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${id}/reopen`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(body),
  });
  if (!res.ok && res.status !== 204) await unwrap(res);
}

export async function redispatchOrder(
  id: string,
  body: { redispatchedBy: string; reason: string; weightFallbackKg?: number },
): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${id}/redispatch`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(body),
  });
  if (!res.ok && res.status !== 204) await unwrap(res);
}

// Phase b11 — operator close-out for orders stranded at an in-flight
// status with 0 active trips. Backend rejects if either precondition
// (order in-flight, trips empty) doesn't hold.
export async function abandonStuckOrder(
  id: string,
  body: { abandonedBy: string; reason: string },
): Promise<void> {
  const res = await fetch(`/api/delivery-orders/${id}/abandon-after-trip-cancel`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify(body),
  });
  if (!res.ok && res.status !== 204) await unwrap(res);
}

export type ResendOmsNotificationResult = {
  shipmentId: string;
  deliveryBy: string;
  lotCount: number;
  latencyMs: number;
};

// Manual resend of the upstream-OMS shipment notification for a specific
// trip. Backend gates: order has OrderRef + trip belongs to order +
// items are bound. Upstream dedupes by shipmentId, so re-firing on a row
// that previously succeeded is safe.
export async function resendOmsNotification(
  orderId: string,
  tripId: string,
  requestedBy?: string,
): Promise<ResendOmsNotificationResult> {
  const res = await fetch(`/api/delivery-orders/${orderId}/trips/${tripId}/notify-oms`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify({ requestedBy: requestedBy ?? null }),
  });
  return unwrap<ResendOmsNotificationResult>(res);
}

export type ResendOmsArrivedNotificationResult = {
  shipmentId: string;
  lotCount: number;
  latencyMs: number;
};

// Manual resend of the OMS "arrived" (drop completed) notification.
// Mirrors resendOmsNotification but hits the /arrived endpoint family.
export async function resendOmsArrivedNotification(
  orderId: string,
  tripId: string,
  requestedBy?: string,
): Promise<ResendOmsArrivedNotificationResult> {
  const res = await fetch(`/api/delivery-orders/${orderId}/trips/${tripId}/notify-oms-arrived`, {
    method: "POST",
    headers: mutationHeaders(),
    body: JSON.stringify({ requestedBy: requestedBy ?? null }),
  });
  return unwrap<ResendOmsArrivedNotificationResult>(res);
}

