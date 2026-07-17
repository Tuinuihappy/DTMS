// Resolves RIOT3 ACT mission codes to the operator-named ActionTemplate.
//
// RIOT3 gives ACT missions no human-readable identity at all — actionName
// arrives as "ACT [4,1,0]" ([vendorActionId, param0, param1]) and
// actionDescription is empty on this deployment. The SAME triple, however,
// is exactly what an ActionTemplate stores (planning side, operator-managed
// CRUD), so the template registry IS the name mapping: "ACT [4,1,0]" →
// "LIFTUP WITH CAMERA". Renaming a template updates historical timelines on
// the next fetch — the registry is referenced live, not snapshotted.
//
// Sibling of riot3-error-codes.ts (same resolve-client-side pattern).

import { useEffect, useState } from "react";
import {
  listActionTemplates,
  type ActionTemplateDto,
} from "@/lib/api/action-templates";

// "ACT [4,1,0]" → "4,1,0". Null when the string doesn't carry the
// bracketed triple (MOVE rows, malformed vendor payloads).
export function parseActCode(actionName: string | null | undefined): string | null {
  if (!actionName) return null;
  const m = /\[\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\]/.exec(actionName);
  return m ? `${m[1]},${m[2]},${m[3]}` : null;
}

export type ActionNameIndex = ReadonlyMap<string, string>;

// Builds "id,param0,param1" → template name. Missing params default to 0
// (matches how dispatch serialises the triple into the RIOT3 actionName).
// Active templates win over inactive on a key collision; param_str is NOT
// part of the key because RIOT3's actionName never carries it — if two
// templates share a triple and differ only by param_str, the first active
// one names the row.
export function buildActionNameIndex(templates: ActionTemplateDto[]): ActionNameIndex {
  const index = new Map<string, string>();
  const byActivity = [...templates].sort(
    (a, b) => Number(a.isActive) - Number(b.isActive), // inactive first, active overwrite
  );
  for (const t of byActivity) {
    const num = (key: string): number => {
      const v = t.actionParameters.find((p) => p.key.toLowerCase() === key)?.value;
      const n = typeof v === "number" ? v : Number(v);
      return Number.isFinite(n) ? n : 0;
    };
    const id = t.actionParameters.some((p) => p.key.toLowerCase() === "id") ? num("id") : null;
    if (id === null || !t.actionName) continue; // templates without a vendor id can't match a code
    index.set(`${id},${num("param0")},${num("param1")}`, t.actionName);
  }
  return index;
}

// Fetch-once hook for timeline renders. includeInactive so a template that
// was deactivated after a trip ran still names that trip's history. Errors
// degrade silently to an empty index — rows fall back to the raw code.
export function useActionNameIndex(enabled: boolean): ActionNameIndex {
  const [index, setIndex] = useState<ActionNameIndex>(new Map());

  useEffect(() => {
    if (!enabled) return;
    const ctrl = new AbortController();
    listActionTemplates(
      { page: 1, size: 500, includeInactive: true },
      ctrl.signal,
    )
      .then((paged) => setIndex(buildActionNameIndex(paged.records)))
      .catch(() => {
        /* silent — raw codes remain readable */
      });
    return () => ctrl.abort();
  }, [enabled]);

  return index;
}
