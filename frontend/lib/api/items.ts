// Client for the DeliveryOrder Items read endpoints
//   GET /api/v1/items          (search, paged)  → backend SearchItemsQuery
//   GET /api/v1/items/{itemId} (detail)         → backend GetItemQuery
// Reached through the same-origin Next proxy (app/api/items/**).

// Item status arrives from the backend as SnakeCaseUpper (global
// JsonStringEnumConverter). The `status` FILTER param, however, is parsed
// server-side with Enum.TryParse on the C# member name, so the filter must
// send the member-name form (e.g. "DroppedOff"), not "DROPPED_OFF".
export type ItemStatusWire =
  | "PENDING"
  | "PICKED"
  | "DROPPED_OFF"
  | "DELIVERED"
  | "FAILED"
  | "RETURNED"
  | "CANCELLED";

export type OrderContext = {
  id: string;
  orderRef: string;
  orderStatus: string;
  priority: string;
};

export type Quantity = { value: number; uom: string };
export type Dimensions = {
  lengthMm: number;
  widthMm: number;
  heightMm: number;
  volumeCBM?: number;
};
export type Hazmat = { classCode: string; packingGroup: string | null };
export type TemperatureRange = { minC: number | null; maxC: number | null };

export type ItemSearchResult = {
  id: string;
  itemSeq: number;
  itemId: string;
  description: string | null;
  pickupLocationCode: string;
  dropLocationCode: string;
  pickupStationId: string | null;
  dropStationId: string | null;
  loadUnitProfileCode: string | null;
  dimensions: Dimensions | null;
  weightKg: number | null;
  quantity: Quantity;
  hazmat: Hazmat | null;
  temperature: TemperatureRange | null;
  handlingInstructions: string[];
  status: ItemStatusWire;
  order: OrderContext;
};

export type ItemDetail = {
  id: string;
  deliveryOrderId: string;
  itemSeq: number;
  itemId: string;
  description: string | null;
  pickupLocationCode: string;
  dropLocationCode: string;
  pickupStationId: string | null;
  dropStationId: string | null;
  loadUnitProfileCode: string | null;
  dimensions: Dimensions | null;
  weightKg: number | null;
  quantity: Quantity;
  hazmat: Hazmat | null;
  temperature: TemperatureRange | null;
  handlingInstructions: string[];
  status: ItemStatusWire;
};

export type PagedResult<T> = {
  data: T[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type ItemFilters = {
  itemId?: string;
  // C# enum member name, e.g. "DroppedOff" (see note above), or undefined.
  status?: string;
  pickupCode?: string;
  dropCode?: string;
  page?: number;
  pageSize?: number;
};

export async function searchItems(
  f: ItemFilters,
  signal?: AbortSignal,
): Promise<PagedResult<ItemSearchResult>> {
  const qs = new URLSearchParams();
  if (f.itemId?.trim()) qs.set("itemId", f.itemId.trim());
  if (f.status) qs.set("status", f.status);
  if (f.pickupCode?.trim()) qs.set("pickupCode", f.pickupCode.trim());
  if (f.dropCode?.trim()) qs.set("dropCode", f.dropCode.trim());
  qs.set("page", String(f.page ?? 1));
  qs.set("pageSize", String(f.pageSize ?? 25));

  const res = await fetch(`/api/items?${qs.toString()}`, {
    cache: "no-store",
    signal,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    throw new Error(text || `Failed to load items (${res.status})`);
  }
  return res.json();
}

export async function getItem(itemId: string): Promise<ItemDetail> {
  const res = await fetch(`/api/items/${itemId}`, { cache: "no-store" });
  if (!res.ok) {
    let message = `Failed to load item (${res.status})`;
    try {
      const body = (await res.json()) as { message?: string };
      if (body?.message) message = body.message;
    } catch {
      // not JSON
    }
    throw new Error(message);
  }
  return res.json();
}

// Filter dropdown options — `value` is the C# member name sent to the
// backend; `label` is the human display.
export const ITEM_STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: "Pending", label: "Pending" },
  { value: "Picked", label: "Picked" },
  { value: "DroppedOff", label: "Dropped off" },
  { value: "Delivered", label: "Delivered" },
  { value: "Failed", label: "Failed" },
  { value: "Returned", label: "Returned" },
  { value: "Cancelled", label: "Cancelled" },
];

// Prettify a SnakeCaseUpper wire status for display (DROPPED_OFF → Dropped off).
export function formatItemStatus(s: string): string {
  const lower = s.replace(/_/g, " ").toLowerCase();
  return lower.charAt(0).toUpperCase() + lower.slice(1);
}
