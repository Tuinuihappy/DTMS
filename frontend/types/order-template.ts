import { z } from "zod";
import { actionParameterDtoSchema, type ActionParameterDto } from "./action-template";

// Mirrors the backend's MissionRequest parser
// (OrderTemplateMissionParser.ParseAll). A mission is either MOVE
// (mapId + stationId required) or ACT (actionTemplateName XOR inline
// actionType + actionParameters). We replicate that XOR rule in zod
// so the form rejects ambiguous input before the network call.

export const missionMoveSchema = z.object({
  type: z.literal("MOVE"),
  category: z.string().optional(),
  mapId: z.coerce.number().int(),
  stationId: z.coerce.number().int(),
  blockingType: z.string().optional(),
});

export const missionActFromTemplateSchema = z.object({
  type: z.literal("ACT"),
  category: z.string().optional(),
  blockingType: z.string().optional(),
  actionTemplateName: z.string().min(1, "pick an ActionTemplate"),
});

export const missionActInlineSchema = z.object({
  type: z.literal("ACT"),
  category: z.string().optional(),
  blockingType: z.string().optional(),
  actionType: z.string().min(1),
  actionParameters: z.array(actionParameterDtoSchema).min(1),
});

// The form keeps a single shape that the UI swaps between via a select.
// We collapse to the wire shape at submit time.
export const missionFormSchema = z.object({
  type: z.enum(["MOVE", "ACT"]),
  // MOVE only
  mapId: z.string().optional(),
  stationId: z.string().optional(),
  // ACT (template ref)
  actionTemplateName: z.string().optional(),
  // ACT (inline)
  actionType: z.string().optional(),
  inlineActionId: z.string().optional(),
  inlineParam0: z.string().optional(),
  inlineParam1: z.string().optional(),
  inlineParamStr: z.string().optional(),
  // Shared
  category: z.string().optional(),
  blockingType: z.string().optional(),
}).superRefine((mission, ctx) => {
  if (mission.type === "MOVE") {
    if (!mission.mapId || !mission.stationId) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "MOVE requires mapId and stationId",
        path: ["mapId"],
      });
    }
    return;
  }
  // ACT — must have EITHER an ActionTemplate ref OR inline params, not both.
  const hasRef = !!mission.actionTemplateName?.trim();
  const hasInline = !!mission.actionType?.trim();
  if (hasRef && hasInline) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      message: "Pick either an ActionTemplate or inline params, not both",
      path: ["actionTemplateName"],
    });
  }
  if (!hasRef && !hasInline) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      message: "Pick an ActionTemplate or fill inline params",
      path: ["actionTemplateName"],
    });
  }
});

export type MissionFormValues = z.infer<typeof missionFormSchema>;

export const createOrderTemplateFormSchema = z.object({
  name: z.string().min(1, "name is required").max(100),
  priority: z.coerce.number().int().min(0).max(100),
  structureType: z.string(),
  transportOrderPriority: z.coerce.number().int().min(0).max(100).optional(),
  missions: z.array(missionFormSchema).min(1, "at least one mission"),
  appointVehicleKey: z.string().optional(),
  appointVehicleName: z.string().optional(),
  appointVehicleGroupKey: z.string().optional(),
  appointVehicleGroupName: z.string().optional(),
  appointQueueWaitArea: z.string().optional(),
  description: z.string().optional(),
});

export type CreateOrderTemplateFormValues = z.infer<
  typeof createOrderTemplateFormSchema
>;

// Wire-format request types ---------------------------------------------------

export interface MissionRequest {
  type: "MOVE" | "ACT";
  category?: string;
  mapId?: number;
  stationId?: number;
  actionType?: string;
  actionTemplateName?: string;
  blockingType?: string;
  actionParameters?: ActionParameterDto[];
}

export interface TransportOrderRequest {
  structureType?: string;
  priority?: number;
  missions: MissionRequest[];
}

export interface CreateOrderTemplateRequest {
  name: string;
  priority: number;
  transportOrder: TransportOrderRequest;
  appointVehicleKey?: string;
  appointVehicleName?: string;
  appointVehicleGroupKey?: string;
  appointVehicleGroupName?: string;
  appointQueueWaitArea?: string;
  description?: string;
}

export type UpdateOrderTemplateRequest = CreateOrderTemplateRequest;

export interface InstantiateOrderTemplateRequest {
  priority?: number;
  appointVehicleKey?: string;
  appointVehicleName?: string;
  appointVehicleGroupKey?: string;
  appointVehicleGroupName?: string;
  appointQueueWaitArea?: string;
  upperKey?: string;
  dryRun?: boolean;
}

// Wire-format response types --------------------------------------------------

export interface OrderTemplateMissionDto {
  type: "MOVE" | "ACT";
  sequenceOrder: number;
  category?: string;
  mapId?: number;
  stationId?: number;
  actionType?: string;
  actionTemplateName?: string;
  blockingType?: string;
  actionParameters?: ActionParameterDto[];
}

export interface OrderTemplateDto {
  id: string;
  name: string;
  priority: number;
  structureType: string;
  transportOrderPriority: number;
  missions: OrderTemplateMissionDto[];
  appointVehicleKey: string | null;
  appointVehicleName: string | null;
  appointVehicleGroupKey: string | null;
  appointVehicleGroupName: string | null;
  appointQueueWaitArea: string | null;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  modifiedAt: string | null;
}

// Helper: convert form values into the wire-format mission shape.
export function missionFormToRequest(m: MissionFormValues): MissionRequest {
  const base: MissionRequest = { type: m.type };
  if (m.category) base.category = m.category;
  if (m.blockingType) base.blockingType = m.blockingType;

  if (m.type === "MOVE") {
    base.mapId = m.mapId ? Number(m.mapId) : undefined;
    base.stationId = m.stationId ? Number(m.stationId) : undefined;
    return base;
  }

  if (m.actionTemplateName?.trim()) {
    base.actionTemplateName = m.actionTemplateName.trim();
    return base;
  }

  base.actionType = m.actionType?.trim();
  const params: ActionParameterDto[] = [];
  if (m.inlineActionId) params.push({ key: "id", value: Number(m.inlineActionId) });
  if (m.inlineParam0) params.push({ key: "param0", value: Number(m.inlineParam0) });
  if (m.inlineParam1) params.push({ key: "param1", value: Number(m.inlineParam1) });
  if (m.inlineParamStr) params.push({ key: "param_str", value: m.inlineParamStr });
  base.actionParameters = params;
  return base;
}

// Reverse: hydrate a saved OrderTemplate back into the form shape.
export function missionToForm(m: OrderTemplateMissionDto): MissionFormValues {
  const base: MissionFormValues = {
    type: m.type,
    category: m.category,
    blockingType: m.blockingType,
  };
  if (m.type === "MOVE") {
    base.mapId = m.mapId?.toString();
    base.stationId = m.stationId?.toString();
    return base;
  }
  if (m.actionTemplateName) {
    base.actionTemplateName = m.actionTemplateName;
    return base;
  }
  base.actionType = m.actionType;
  for (const p of m.actionParameters ?? []) {
    const v = p.value?.toString() ?? "";
    if (p.key === "id") base.inlineActionId = v;
    else if (p.key === "param0") base.inlineParam0 = v;
    else if (p.key === "param1") base.inlineParam1 = v;
    else if (p.key === "param_str") base.inlineParamStr = v;
  }
  return base;
}
