import { z } from "zod";

// Backend contract — these mirror the C# DTOs in
// d:\DTMS\src\Modules\Planning\AMR.DeliveryPlanning.Planning.Presentation\PlanningEndpoints.cs
// (CreateActionTemplateRequest, ActionParameterDto). Keeping the field
// names lowercase matches the JSON wire format.

export const actionParameterDtoSchema = z.object({
  key: z.string(),
  value: z.union([z.number(), z.string()]).optional(),
});

export type ActionParameterDto = z.infer<typeof actionParameterDtoSchema>;

// The backend's ActionParameterParser requires id/param0/param1 as ints
// and treats param_str as optional. We validate the same invariants in
// the form so users get inline errors before the round-trip.
const REQUIRED_KEYS = ["id", "param0", "param1"] as const;

export const createActionTemplateFormSchema = z.object({
  actionName: z
    .string()
    .min(1, "name is required")
    .max(100, "name must be 100 chars or less"),
  actionType: z.string().optional(),
  id: z.coerce.number().int(),
  param0: z.coerce.number().int(),
  param1: z.coerce.number().int(),
  paramStr: z.string().optional().or(z.literal("")),
  description: z.string().optional().or(z.literal("")),
});

export type CreateActionTemplateFormValues = z.infer<
  typeof createActionTemplateFormSchema
>;

// Shape POSTed to the backend. We assemble the actionParameters array
// from the flat form values so the operator never has to think in
// key/value terms.
export interface CreateActionTemplateRequest {
  actionName: string;
  actionType?: string;
  actionParameters: ActionParameterDto[];
  description?: string;
}

export interface UpdateActionTemplateRequest {
  actionType?: string;
  actionParameters: ActionParameterDto[];
  description?: string;
}

// Mirrors the C# ActionTemplate aggregate's read-model (returned by
// GET /api/v1/planning/action-templates).
export interface ActionTemplateDto {
  id: string;
  name: string;
  actionType: string;
  vendorActionId: number;
  param0: number;
  param1: number;
  paramStr: string | null;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  modifiedAt: string | null;
}

// Helper used by the form ↔ request bridge: flatten the four
// well-known fields into the API's key/value array shape.
export function formToActionParameters(
  values: Pick<
    CreateActionTemplateFormValues,
    "id" | "param0" | "param1" | "paramStr"
  >
): ActionParameterDto[] {
  const params: ActionParameterDto[] = [
    { key: "id", value: values.id },
    { key: "param0", value: values.param0 },
    { key: "param1", value: values.param1 },
  ];
  if (values.paramStr && values.paramStr.length > 0) {
    params.push({ key: "param_str", value: values.paramStr });
  }
  return params;
}

// Reverse direction: parse the array back into form-ready primitives.
// Used when opening the edit dialog on an existing template.
export function actionParametersToForm(template: ActionTemplateDto): {
  id: number;
  param0: number;
  param1: number;
  paramStr: string;
} {
  return {
    id: template.vendorActionId,
    param0: template.param0,
    param1: template.param1,
    paramStr: template.paramStr ?? "",
  };
}

// Keep REQUIRED_KEYS exported so the OrderTemplate form can re-use it
// when validating inline ACT missions.
export { REQUIRED_KEYS };
