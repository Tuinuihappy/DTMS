import { z } from "zod";

// Mirrors the C# DTOs at:
//   d:\DTMS\src\Modules\DeliveryOrder\AMR.DeliveryPlanning.DeliveryOrder.Presentation\DeliveryOrderEndpoints.cs
// and the domain at:
//   d:\DTMS\src\Modules\DeliveryOrder\AMR.DeliveryPlanning.DeliveryOrder.Domain\Entities\DeliveryOrder.cs
//
// IMPORTANT — the API serializes enums via System.Text.Json's
// JsonStringEnumConverter with the SnakeCaseUpper naming policy
// (configured in Program.cs). So `Priority.Normal` lands as
// "NORMAL" on the wire, `OrderStatus.InProgress` as "IN_PROGRESS",
// `HandlingInstruction.ThisSideUp` as "THIS_SIDE_UP", etc.
// We use those exact wire strings as the source of truth on the TS
// side too and convert to a human-readable label only at render
// time via formatEnumLabel().
//
// Also: the list endpoint returns `{ data, page, pageSize, totalCount,
// totalPages }` and each item exposes the status via `orderStatus`,
// NOT `status` — this caused the "no orders" empty state even when
// the API had data.

// ── Enums (canonical wire values) ───────────────────────────────────────

export const PRIORITY_VALUES = ["LOW", "NORMAL", "HIGH", "CRITICAL"] as const;
export type Priority = (typeof PRIORITY_VALUES)[number];

// UOM values are already uppercase in C# so SnakeCaseUpper is a no-op.
export const UOM_VALUES = [
  "KG",
  "G",
  "LB",
  "EA",
  "BOX",
  "PALLET",
  "CASE",
] as const;
export type UnitOfMeasure = (typeof UOM_VALUES)[number];

export const PACKING_GROUP_VALUES = ["I", "II", "III"] as const;
export type PackingGroup = (typeof PACKING_GROUP_VALUES)[number];

export const HANDLING_VALUES = [
  "FRAGILE",
  "THIS_SIDE_UP",
  "DO_NOT_STACK",
  "HEAVY_LIFT",
  "SHARP",
  "KEEP_DRY",
  "KEEP_DARK",
  "PINCH_HAZARD",
] as const;
export type HandlingInstruction = (typeof HANDLING_VALUES)[number];

export const ORDER_STATUS_VALUES = [
  "DRAFT",
  "SUBMITTED",
  "VALIDATED",
  "CONFIRMED",
  "PLANNING",
  "PLANNED",
  "DISPATCHED",
  "IN_PROGRESS",
  "COMPLETED",
  "HELD",
  "REJECTED",
  "CANCELLED",
  "AMENDED",
  "FAILED",
] as const;
export type OrderStatus = (typeof ORDER_STATUS_VALUES)[number];

export const ITEM_STATUS_VALUES = [
  "PENDING",
  "PICKED",
  "DELIVERED",
  "FAILED",
  "RETURNED",
  "CANCELLED",
] as const;
export type ItemStatus = (typeof ITEM_STATUS_VALUES)[number];

// Statuses where lifecycle actions are still possible (Sheet shows
// buttons). The other statuses (Completed / Failed / Cancelled /
// Rejected) render a read-only sheet.
export const TERMINAL_STATUSES: OrderStatus[] = [
  "COMPLETED",
  "FAILED",
  "CANCELLED",
  "REJECTED",
];

// Converts a wire-format enum value like "IN_PROGRESS" or "FRAGILE"
// into a human-readable label: "In Progress" / "Fragile". Used by
// badges, pills, and dropdown options.
export function formatEnumLabel(value: string): string {
  return value
    .split("_")
    .map((w) =>
      w.length === 0 ? "" : w[0].toUpperCase() + w.slice(1).toLowerCase()
    )
    .join(" ");
}

// ── Form / request schemas ──────────────────────────────────────────────

export const serviceWindowSchema = z
  .object({
    earliestUtc: z.string().optional().or(z.literal("")),
    latestUtc: z.string().optional().or(z.literal("")),
  })
  .superRefine((sw, ctx) => {
    const e = sw.earliestUtc?.trim() || null;
    const l = sw.latestUtc?.trim() || null;
    if (!e && !l) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Set at least one bound (earliest or latest).",
        path: ["earliestUtc"],
      });
      return;
    }
    if (e && l && new Date(e) > new Date(l)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Earliest must be ≤ latest.",
        path: ["earliestUtc"],
      });
    }
  });

export const itemFormSchema = z
  .object({
    itemId: z.string().min(1, "itemId is required").max(100),
    description: z.string().max(500).optional().or(z.literal("")),
    pickupLocationCode: z
      .string()
      .min(1, "pickup code is required")
      .max(50),
    dropLocationCode: z.string().min(1, "drop code is required").max(50),
    loadUnitProfileCode: z.string().max(50).optional().or(z.literal("")),
    weightKg: z.string().optional().or(z.literal("")),
    quantityValue: z.string().min(1, "qty required"),
    quantityUom: z.enum(UOM_VALUES),
    // Advanced — all optional
    lengthMm: z.string().optional().or(z.literal("")),
    widthMm: z.string().optional().or(z.literal("")),
    heightMm: z.string().optional().or(z.literal("")),
    hazmatClass: z.string().optional().or(z.literal("")),
    hazmatPackingGroup: z.enum(PACKING_GROUP_VALUES).optional(),
    temperatureMinC: z.string().optional().or(z.literal("")),
    temperatureMaxC: z.string().optional().or(z.literal("")),
    handling: z.array(z.enum(HANDLING_VALUES)).optional(),
  })
  .superRefine((it, ctx) => {
    if (it.pickupLocationCode.trim() === it.dropLocationCode.trim()) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Pickup and drop codes must differ.",
        path: ["dropLocationCode"],
      });
    }
    if (Number(it.quantityValue) <= 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Quantity must be > 0.",
        path: ["quantityValue"],
      });
    }
    if (it.weightKg && it.weightKg.trim() !== "" && Number(it.weightKg) <= 0) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Weight must be > 0 when set.",
        path: ["weightKg"],
      });
    }
    if (
      it.hazmatClass &&
      it.hazmatClass.trim() !== "" &&
      !/^[1-9](\.[1-6])?$/.test(it.hazmatClass.trim())
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Hazmat class must look like '3' or '2.1'.",
        path: ["hazmatClass"],
      });
    }
    const min = it.temperatureMinC?.trim() || null;
    const max = it.temperatureMaxC?.trim() || null;
    if (min && max && Number(min) > Number(max)) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "Min must be ≤ max.",
        path: ["temperatureMinC"],
      });
    }
  });

export type ItemFormValues = z.infer<typeof itemFormSchema>;

export const createDraftDeliveryOrderFormSchema = z.object({
  orderRef: z.string().min(1, "orderRef is required").max(200),
  priority: z.enum(PRIORITY_VALUES),
  serviceWindow: serviceWindowSchema,
  requestedBy: z.string().max(200).optional().or(z.literal("")),
  notes: z.string().max(1000).optional().or(z.literal("")),
  items: z.array(itemFormSchema).min(1, "at least one item is required"),
});

export type CreateDraftDeliveryOrderFormValues = z.infer<
  typeof createDraftDeliveryOrderFormSchema
>;

// ── Wire-format types (what we actually POST / receive) ─────────────────

export interface ServiceWindowDto {
  earliestUtc?: string | null;
  latestUtc?: string | null;
}

export interface DimensionsDto {
  lengthMm: number;
  widthMm: number;
  heightMm: number;
  // Detail responses include the computed volume; the request shape
  // ignores it (server recomputes).
  volumeCBM?: number;
}

export interface HazmatDto {
  classCode: string;
  packingGroup?: PackingGroup;
}

export interface TemperatureDto {
  minC?: number | null;
  maxC?: number | null;
}

export interface QuantityDto {
  value: number;
  uom: UnitOfMeasure;
}

export interface CreateItemRequest {
  itemId: string;
  description?: string;
  pickupLocationCode: string;
  dropLocationCode: string;
  loadUnitProfileCode?: string;
  dimensions?: DimensionsDto;
  weightKg?: number;
  quantity: QuantityDto;
  hazmat?: HazmatDto;
  temperature?: TemperatureDto;
  handlingInstructions?: HandlingInstruction[];
}

export interface CreateDraftDeliveryOrderRequest {
  orderRef: string;
  priority: Priority;
  serviceWindow?: ServiceWindowDto;
  requestedBy?: string;
  notes?: string;
  items: CreateItemRequest[];
}

// ── Response DTOs ───────────────────────────────────────────────────────

export interface ItemDto {
  id: string;
  itemSeq: number;
  itemId: string;
  description: string | null;
  pickupLocationCode: string;
  dropLocationCode: string;
  pickupStationId: string | null;
  dropStationId: string | null;
  loadUnitProfileCode: string | null;
  dimensions: DimensionsDto | null;
  weightKg: number | null;
  quantity: QuantityDto;
  hazmat: HazmatDto | null;
  temperature: TemperatureDto | null;
  handlingInstructions: HandlingInstruction[] | null;
  // Detail items don't include status (they're embedded inside the
  // order); the items endpoint does.
  status?: ItemStatus;
}

export interface DeliveryOrderListDto {
  id: string;
  orderRef: string;
  sourceSystem: string;
  priority: Priority;
  // ⚠️ field name is `orderStatus`, NOT `status` — this is the
  // most-recently-discovered API contract surprise.
  orderStatus: OrderStatus;
  totalItems: number;
  totalWeightKg: number;
  totalQuantity: number;
  serviceWindow: ServiceWindowDto | null;
  createdBy: string | null;
  requestedBy: string | null;
  notes: string | null;
  createdDate: string;
  updatedDate: string | null;
  submittedAt: string | null;
}

export interface DeliveryOrderDetailDto extends DeliveryOrderListDto {
  items: ItemDto[];
}

export interface OrderQualityIssue {
  code: string;
  severity: "warning" | "error";
  field: string;
  message: string;
}

export interface LifecycleResult {
  orderId: string;
  warnings: OrderQualityIssue[];
}

// Field is `data`, NOT `items`. (The shadcn TanStack Query patterns
// elsewhere call list items "items", but the C# PagedResult wraps
// them as `data`.)
export interface PagedResult<T> {
  data: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// ── form → wire converter ───────────────────────────────────────────────

function nonEmpty(s: string | undefined | null): string | undefined {
  if (!s) return undefined;
  const t = s.trim();
  return t.length === 0 ? undefined : t;
}

function asPositiveNumber(s: string | undefined | null): number | undefined {
  const t = nonEmpty(s);
  if (t === undefined) return undefined;
  const n = Number(t);
  return Number.isFinite(n) ? n : undefined;
}

function itemFormToRequest(it: ItemFormValues): CreateItemRequest {
  const dims =
    it.lengthMm && it.widthMm && it.heightMm
      ? {
          lengthMm: Number(it.lengthMm),
          widthMm: Number(it.widthMm),
          heightMm: Number(it.heightMm),
        }
      : undefined;

  const hazmatClass = nonEmpty(it.hazmatClass);
  const hazmat: HazmatDto | undefined = hazmatClass
    ? {
        classCode: hazmatClass,
        packingGroup: it.hazmatPackingGroup,
      }
    : undefined;

  const tempMinSet = nonEmpty(it.temperatureMinC) !== undefined;
  const tempMaxSet = nonEmpty(it.temperatureMaxC) !== undefined;
  const temperature: TemperatureDto | undefined =
    tempMinSet || tempMaxSet
      ? {
          minC: tempMinSet ? Number(it.temperatureMinC) : null,
          maxC: tempMaxSet ? Number(it.temperatureMaxC) : null,
        }
      : undefined;

  return {
    itemId: it.itemId.trim(),
    description: nonEmpty(it.description),
    pickupLocationCode: it.pickupLocationCode.trim(),
    dropLocationCode: it.dropLocationCode.trim(),
    loadUnitProfileCode: nonEmpty(it.loadUnitProfileCode),
    dimensions: dims,
    weightKg: asPositiveNumber(it.weightKg),
    quantity: {
      value: Number(it.quantityValue),
      uom: it.quantityUom,
    },
    hazmat,
    temperature,
    handlingInstructions:
      it.handling && it.handling.length > 0 ? it.handling : undefined,
  };
}

export function formToCreateRequest(
  values: CreateDraftDeliveryOrderFormValues
): CreateDraftDeliveryOrderRequest {
  const sw = values.serviceWindow;
  const earliest = nonEmpty(sw.earliestUtc);
  const latest = nonEmpty(sw.latestUtc);
  const serviceWindow: ServiceWindowDto | undefined =
    earliest || latest
      ? {
          earliestUtc: earliest ? new Date(earliest).toISOString() : null,
          latestUtc: latest ? new Date(latest).toISOString() : null,
        }
      : undefined;

  return {
    orderRef: values.orderRef.trim(),
    priority: values.priority,
    serviceWindow,
    requestedBy: nonEmpty(values.requestedBy),
    notes: nonEmpty(values.notes),
    items: values.items.map(itemFormToRequest),
  };
}

export const blankItem: ItemFormValues = {
  itemId: "",
  description: "",
  pickupLocationCode: "",
  dropLocationCode: "",
  loadUnitProfileCode: "",
  weightKg: "",
  quantityValue: "1",
  quantityUom: "EA",
  lengthMm: "",
  widthMm: "",
  heightMm: "",
  hazmatClass: "",
  hazmatPackingGroup: undefined,
  temperatureMinC: "",
  temperatureMaxC: "",
  handling: [],
};
