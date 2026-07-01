// Mirror of src/Modules/Iam/DTMS.Iam.Application/Authorization/StandardSystemPermissions.cs.
// Same list is auto-seeded server-side when a SystemClient is created.
// Used in the Grant Permission modal to surface the standard codes as
// first-class options even though they're runtime-resolved templates
// that don't appear in the static iam.permissions catalog.
export const STANDARD_SYSTEM_PERMISSION_TEMPLATES = [
  "dtms:source:{key}:order:write",
  "dtms:source:{key}:order:read",
] as const;

export function resolveStandardSystemPermissions(systemKey: string): string[] {
  return STANDARD_SYSTEM_PERMISSION_TEMPLATES.map((t) => t.replace("{key}", systemKey));
}
