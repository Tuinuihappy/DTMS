"use client";

import { AnimatePresence, motion, Reorder, useDragControls } from "motion/react";
import {
  AlertTriangle,
  ArrowDown,
  ArrowLeft,
  ArrowUp,
  Check,
  ChevronDown,
  Copy,
  GripVertical,
  Layers,
  Map as MapIcon,
  Navigation,
  Plus,
  Save,
  Sparkles,
  Tag,
  Trash2,
  Truck,
  Workflow,
  X,
} from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import {
  createOrderTemplate,
  updateOrderTemplate,
  type MissionParameterDto,
  type MissionPayload,
  type MissionType,
  type OrderTemplateDto,
  type StructureType,
} from "@/lib/api/order-templates";
import {
  useMapVendorOptions,
  useStationOptions,
  useStationVendorOptions,
  type MapVendorOption,
  type StationOption,
  type StationVendorOption,
} from "@/lib/api/facility";
import { StationCombobox } from "@/components/primitives/station-combobox";
import {
  IntIdCombobox,
  type IntIdOption,
} from "@/components/primitives/int-id-combobox";
import { ActionTemplateCombobox } from "@/components/primitives/action-template-combobox";
import {
  useActionTemplateOptions,
  type ActionTemplateOption,
} from "@/lib/api/action-templates";

// Editor mission shape — uses string inputs for numeric fields so the
// form mirrors what the user actually types (empty input ≠ 0). Parsed to
// nullable numbers in the payload mapper before submit.
type MissionForm = {
  uid: string;
  type: MissionType;
  category: string;
  mapId: string;
  stationId: string;
  // For ACT: prefer referencing an ActionTemplate by name. Inline path uses
  // actionType + actionParameters. We let the user pick the variant.
  actionVariant: "reference" | "inline";
  actionTemplateName: string;
  actionType: string;
  actionParameters: { key: string; value: string }[];
};

type FormState = {
  name: string;
  priority: string;
  description: string;
  structureType: StructureType;
  pickupStationCode: string;
  dropStationCode: string;
  appointVehicleKey: string;
  appointVehicleName: string;
  appointVehicleGroupKey: string;
  appointVehicleGroupName: string;
  appointQueueWaitArea: string;
  missions: MissionForm[];
};

function freshUid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return `m-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

function emptyMission(type: MissionType): MissionForm {
  return {
    uid: freshUid(),
    type,
    category: "",
    mapId: "",
    stationId: "",
    actionVariant: "reference",
    actionTemplateName: "",
    actionType: "",
    actionParameters: [],
  };
}

function emptyForm(): FormState {
  return {
    name: "",
    priority: "5",
    description: "",
    structureType: "sequence",
    pickupStationCode: "",
    dropStationCode: "",
    appointVehicleKey: "",
    appointVehicleName: "",
    appointVehicleGroupKey: "",
    appointVehicleGroupName: "",
    appointQueueWaitArea: "",
    missions: [emptyMission("MOVE")],
  };
}

function formFromDto(t: OrderTemplateDto): FormState {
  const missions = (t.transportOrder?.missions ?? []).map((m) => {
    const params: { key: string; value: string }[] = (m.actionParameters ?? []).map(
      (p) => ({
        key: p.key,
        value: p.value == null ? "" : String(p.value),
      }),
    );
    return {
      uid: freshUid(),
      type: m.type,
      category: m.category ?? "",
      mapId: m.mapId == null ? "" : String(m.mapId),
      stationId: m.stationId == null ? "" : String(m.stationId),
      actionVariant: (m.actionTemplateName ? "reference" : "inline") as
        | "reference"
        | "inline",
      actionTemplateName: m.actionTemplateName ?? "",
      actionType: m.actionType ?? "",
      actionParameters: params,
    };
  });
  return {
    name: t.name,
    priority: String(t.priority),
    description: t.description ?? "",
    structureType: (t.transportOrder?.structureType as StructureType) ?? "sequence",
    pickupStationCode: "",
    dropStationCode: "",
    appointVehicleKey: t.appointVehicleKey ?? "",
    appointVehicleName: t.appointVehicleName ?? "",
    appointVehicleGroupKey: t.appointVehicleGroupKey ?? "",
    appointVehicleGroupName: t.appointVehicleGroupName ?? "",
    appointQueueWaitArea: t.appointQueueWaitArea ?? "",
    missions: missions.length > 0 ? missions : [emptyMission("MOVE")],
  };
}

function parseIntOrNull(s: string): number | null {
  const trimmed = s.trim();
  if (trimmed === "") return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? Math.trunc(n) : null;
}

function parseValue(s: string): string | number | boolean | null {
  if (s.trim() === "") return null;
  if (s === "true") return true;
  if (s === "false") return false;
  const n = Number(s);
  if (Number.isFinite(n) && s.trim() === String(n)) return n;
  return s;
}

function toMissionPayload(m: MissionForm): MissionPayload {
  const base: MissionPayload = {
    type: m.type,
    category: m.category.trim() || null,
  };
  if (m.type === "MOVE") {
    base.mapId = parseIntOrNull(m.mapId);
    base.stationId = parseIntOrNull(m.stationId);
    return base;
  }
  if (m.actionVariant === "reference") {
    base.actionTemplateName = m.actionTemplateName.trim() || null;
    return base;
  }
  // inline ACT
  base.actionType = m.actionType.trim() || null;
  const params: MissionParameterDto[] = m.actionParameters
    .filter((p) => p.key.trim() !== "")
    .map((p) => ({ key: p.key.trim(), value: parseValue(p.value) }));
  base.actionParameters = params.length > 0 ? params : null;
  return base;
}

function validate(form: FormState): string | null {
  if (form.name.trim() === "") return "Name is required.";
  const priority = parseIntOrNull(form.priority);
  if (priority == null) return "Priority must be a number.";
  if (form.missions.length === 0) return "At least one mission is required.";
  for (let i = 0; i < form.missions.length; i++) {
    const m = form.missions[i];
    if (m.type === "ACT") {
      if (m.actionVariant === "reference" && m.actionTemplateName.trim() === "") {
        return `Mission #${i + 1}: pick an Action template or switch to inline.`;
      }
      if (m.actionVariant === "inline" && m.actionType.trim() === "") {
        return `Mission #${i + 1}: actionType is required for inline ACT.`;
      }
    }
  }
  return null;
}

export function TemplateEditor({
  existing,
  duplicating,
}: {
  existing?: OrderTemplateDto | null;
  // Source template when the user is duplicating. The editor runs in
  // create mode (submit goes to createOrderTemplate) but pre-fills from
  // this template and shows a "copied-from" banner. Ignored if
  // `existing` is also set — edit wins.
  duplicating?: OrderTemplateDto | null;
}) {
  const router = useRouter();
  const isEdit = !!existing;
  const isDuplicate = !isEdit && !!duplicating;
  // Source DTO whose values seed the form when present. The editor
  // treats edit and duplicate identically up until submit — both
  // pre-fill from a DTO, only the verb at the end differs.
  const sourceDto = existing ?? duplicating ?? null;

  const [form, setForm] = useState<FormState>(() => {
    if (existing) return formFromDto(existing);
    if (duplicating) {
      const base = formFromDto(duplicating);
      return { ...base, name: `${base.name} (Copy)` };
    }
    return emptyForm();
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  // Shared station catalog — both pickup and drop comboboxes read from
  // this single fetch so the API is hit at most once per editor mount.
  const stations = useStationOptions();
  // Shared map + vendor-id station catalogs for MOVE-row dropdowns.
  // Both lists are fetched once per editor mount and reused across rows.
  const mapVendors = useMapVendorOptions();
  const stationVendors = useStationVendorOptions();
  // Shared action-template catalog for ACT-reference rows.
  const actionTemplates = useActionTemplateOptions();

  useEffect(() => {
    if (existing) {
      setForm(formFromDto(existing));
    } else if (duplicating) {
      const base = formFromDto(duplicating);
      setForm({ ...base, name: `${base.name} (Copy)` });
    }
  }, [existing, duplicating]);

  // Pickup/drop codes aren't in the read DTO (only the resolved
  // station ids are), so on edit-mode entry we resolve them from the
  // shared station catalog once it loads. Run-once per template id so
  // a user clearing or retyping the field doesn't get clobbered when
  // stations re-fetch. Duplicate mode hits the same path — the codes
  // come from the source template's resolved ids.
  const stationCodeAutoFilledForRef = useRef<string | null>(null);
  useEffect(() => {
    if (!sourceDto) return;
    if (stations.loading || stations.error) return;
    if (stationCodeAutoFilledForRef.current === sourceDto.id) return;
    stationCodeAutoFilledForRef.current = sourceDto.id;
    const pickup = sourceDto.pickupStationId
      ? (stations.byId.get(sourceDto.pickupStationId) ?? "")
      : "";
    const drop = sourceDto.dropStationId
      ? (stations.byId.get(sourceDto.dropStationId) ?? "")
      : "";
    if (!pickup && !drop) return;
    setForm((f) => ({
      ...f,
      pickupStationCode: f.pickupStationCode || pickup,
      dropStationCode: f.dropStationCode || drop,
    }));
  }, [sourceDto, stations.loading, stations.error, stations.byId]);
  const moveMissionCount = form.missions.filter((m) => m.type === "MOVE").length;
  const actMissionCount = form.missions.length - moveMissionCount;

  function setMission(uid: string, patch: Partial<MissionForm>) {
    setForm((f) => ({
      ...f,
      missions: f.missions.map((m) => (m.uid === uid ? { ...m, ...patch } : m)),
    }));
  }
  function addMission(type: MissionType) {
    setForm((f) => ({ ...f, missions: [...f.missions, emptyMission(type)] }));
  }
  function removeMission(uid: string) {
    setForm((f) => ({ ...f, missions: f.missions.filter((m) => m.uid !== uid) }));
  }
  function moveMission(uid: string, dir: -1 | 1) {
    setForm((f) => {
      const idx = f.missions.findIndex((m) => m.uid === uid);
      const ni = idx + dir;
      if (idx < 0 || ni < 0 || ni >= f.missions.length) return f;
      const out = [...f.missions];
      const [item] = out.splice(idx, 1);
      out.splice(ni, 0, item);
      return { ...f, missions: out };
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setValidationError(null);
    const v = validate(form);
    if (v) {
      setValidationError(v);
      return;
    }
    setBusy(true);
    try {
      const priority = parseIntOrNull(form.priority) ?? 0;
      const payloadBase = {
        priority,
        transportOrder: {
          structureType: form.structureType,
          priority,
          missions: form.missions.map(toMissionPayload),
        },
        appointVehicleKey: form.appointVehicleKey.trim() || null,
        appointVehicleName: form.appointVehicleName.trim() || null,
        appointVehicleGroupKey: form.appointVehicleGroupKey.trim() || null,
        appointVehicleGroupName: form.appointVehicleGroupName.trim() || null,
        appointQueueWaitArea: form.appointQueueWaitArea.trim() || null,
        description: form.description.trim() || null,
        pickupStationCode: form.pickupStationCode.trim() || null,
        dropStationCode: form.dropStationCode.trim() || null,
      };

      if (isEdit && existing) {
        await updateOrderTemplate(existing.id, payloadBase);
        router.push(`/delivery-orders/order-templates?updated=${existing.id}`);
        router.refresh();
      } else {
        // Both fresh-create and duplicate land here — duplicate just
        // happens to have a pre-filled form. The server sees an
        // ordinary create POST either way.
        const created = await createOrderTemplate({
          ...payloadBase,
          name: form.name.trim(),
        });
        router.push(`/delivery-orders/order-templates?created=${created.id}`);
        router.refresh();
      }
    } catch (err) {
      setError((err as Error).message || "Save failed.");
      setBusy(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-7">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <Link
            href="/delivery-orders/order-templates"
            className="grid h-10 w-10 cursor-pointer place-items-center rounded-[14px] bg-white/70 text-[var(--color-ink-700)] transition-colors hover:bg-white/85 dark:bg-white/[0.06] dark:hover:bg-white/[0.1]"
            aria-label="Back to templates"
          >
            <ArrowLeft className="h-4 w-4" strokeWidth={2.2} />
          </Link>
          <div className="min-w-0">
            <div className="text-[10.5px] font-semibold uppercase tracking-[0.14em] text-[var(--color-ink-400)]">
              {isEdit
                ? "Edit order template"
                : isDuplicate
                  ? "Duplicate order template"
                  : "New order template"}
            </div>
            <h1 className="font-display mt-1 text-[1.6rem] font-semibold text-[var(--color-ink-900)] sm:text-[1.8rem]">
              {isEdit
                ? existing!.name
                : isDuplicate
                  ? `Duplicate of ${duplicating!.name}`
                  : "Compose a reusable order recipe"}
            </h1>
            <p className="mt-1 max-w-md text-[12.5px] text-[var(--color-ink-500)]">
              {isDuplicate
                ? "Review and edit the copied data, then save as a new template. The original is unchanged."
                : "Author missions once, then dispatch live RIOT3 orders in one click — with optional overrides and a dry-run preview."}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <Link
            href="/delivery-orders/order-templates"
            className="rounded-full px-4 py-2 text-[12.5px] font-semibold text-[var(--color-ink-600)] transition-colors hover:bg-white/55 dark:hover:bg-white/[0.06]"
          >
            Cancel
          </Link>
          <motion.button
            type="submit"
            whileTap={!busy ? { scale: 0.96 } : undefined}
            whileHover={!busy ? { y: -1 } : undefined}
            disabled={busy}
            className="inline-flex cursor-pointer items-center gap-1.5 rounded-full bg-[var(--color-brand-900)] px-5 py-2 text-[12.5px] font-semibold text-white shadow-[0_8px_24px_-8px_rgba(15,23,42,0.55)] transition-shadow disabled:cursor-not-allowed disabled:opacity-50 dark:bg-[var(--color-brand-500)]"
          >
            {busy ? (
              <motion.span
                animate={{ rotate: 360 }}
                transition={{ duration: 1, repeat: Infinity, ease: "linear" }}
                className="inline-block h-3.5 w-3.5 rounded-full border-2 border-white/40 border-t-white"
                aria-hidden
              />
            ) : (
              <Save className="h-3.5 w-3.5" strokeWidth={2.3} />
            )}
            {busy
              ? isEdit
                ? "Saving…"
                : isDuplicate
                  ? "Creating copy…"
                  : "Creating…"
              : isEdit
                ? "Save changes"
                : isDuplicate
                  ? "Create copy"
                  : "Create template"}
          </motion.button>
        </div>
      </div>

      {isDuplicate && (
        <motion.div
          initial={{ opacity: 0, y: -6 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex items-start gap-2.5 rounded-[var(--radius-sm)] border border-[var(--color-brand-500)]/25 bg-[var(--color-pastel-sky)]/50 px-3.5 py-2.5 text-[12.5px] text-[var(--color-brand-900)] dark:border-[var(--color-brand-500)]/30 dark:bg-[var(--color-brand-500)]/[0.08] dark:text-[var(--color-pastel-sky-ink)]"
        >
          <Copy className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.2} />
          <div className="min-w-0">
            <div className="font-semibold">Duplicating an existing template</div>
            <div className="mt-0.5 text-[11.5px] opacity-80">
              Original:{" "}
              <span className="font-mono font-semibold">{duplicating!.name}</span>.
              All fields are pre-filled — change anything before saving as a new
              template.
            </div>
          </div>
        </motion.div>
      )}

      {(error || validationError) && (
        <motion.div
          initial={{ opacity: 0, y: -6 }}
          animate={{ opacity: 1, y: 0 }}
          className="flex items-start gap-2 rounded-[var(--radius-sm)] border border-[var(--color-coral)]/40 bg-[var(--color-coral)]/10 px-3 py-2 text-[12.5px] text-[var(--color-coral)]"
        >
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" strokeWidth={2.2} />
          <span>{validationError ?? error}</span>
        </motion.div>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Left column — primary fields */}
        <div className="space-y-6 lg:col-span-2">
          <EditorCard
            title="Basics"
            icon={<Sparkles className="h-3.5 w-3.5" strokeWidth={2.3} />}
          >
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <FieldText
                label="Name"
                required
                placeholder="e.g. Cross-dock to Bay 3"
                value={form.name}
                onChange={(v) => setForm({ ...form, name: v })}
                disabled={isEdit}
                helper={isEdit ? "Name is immutable after creation" : undefined}
                className="sm:col-span-2"
              />
              <FieldNumber
                label="Priority"
                required
                placeholder="0–9"
                value={form.priority}
                onChange={(v) => setForm({ ...form, priority: v })}
              />
              <FieldText
                label="Description"
                placeholder="Optional notes for operators"
                value={form.description}
                onChange={(v) => setForm({ ...form, description: v })}
                className="sm:col-span-3"
              />
              <FieldSelect
                label="Structure"
                value={form.structureType}
                onChange={(v) =>
                  setForm({ ...form, structureType: v as StructureType })
                }
                options={[
                  { value: "sequence", label: "Sequence — missions run in order" },
                  { value: "parallel", label: "Parallel — missions run together" },
                ]}
              />
              <FieldStation
                label="Pickup station code"
                placeholder="Search pickup station…"
                value={form.pickupStationCode}
                onChange={(v) => setForm({ ...form, pickupStationCode: v })}
                stations={stations.stations}
                loading={stations.loading}
                error={stations.error}
              />
              <FieldStation
                label="Drop station code"
                placeholder="Search drop station…"
                value={form.dropStationCode}
                onChange={(v) => setForm({ ...form, dropStationCode: v })}
                stations={stations.stations}
                loading={stations.loading}
                error={stations.error}
              />
            </div>
          </EditorCard>

          <EditorCard
            title={`Mission journey · ${form.missions.length}`}
            icon={<Workflow className="h-3.5 w-3.5" strokeWidth={2.3} />}
            action={
              <div className="flex items-center gap-1.5">
                <AddMissionBtn type="MOVE" onAdd={() => addMission("MOVE")} />
                <AddMissionBtn type="ACT" onAdd={() => addMission("ACT")} />
              </div>
            }
          >
            <div className="mb-3 flex flex-wrap items-center gap-1.5 text-[11px] text-[var(--color-ink-500)]">
              <CountPill tone="mint" label={`${moveMissionCount} MOVE`} />
              <CountPill tone="peach" label={`${actMissionCount} ACT`} />
              <span className="text-[var(--color-ink-400)]">
                · structure: {form.structureType}
              </span>
            </div>

            <Reorder.Group
              axis="y"
              as="div"
              values={form.missions}
              onReorder={(next) => setForm((f) => ({ ...f, missions: next }))}
              className="space-y-2.5"
            >
              <AnimatePresence initial={false}>
                {form.missions.map((m, i) => (
                  <MissionRow
                    key={m.uid}
                    mission={m}
                    index={i}
                    total={form.missions.length}
                    onChange={(patch) => setMission(m.uid, patch)}
                    onRemove={() => removeMission(m.uid)}
                    onMove={(dir) => moveMission(m.uid, dir)}
                    mapVendors={mapVendors}
                    stationVendors={stationVendors}
                    actionTemplates={actionTemplates}
                  />
                ))}
              </AnimatePresence>
              {form.missions.length === 0 && (
                <div className="rounded-[var(--radius-sm)] border border-dashed border-[var(--color-ink-200)] bg-white/40 px-4 py-6 text-center text-[12.5px] italic text-[var(--color-ink-500)] dark:border-white/[0.08] dark:bg-white/[0.02]">
                  Add at least one mission to dispatch this template.
                </div>
              )}
            </Reorder.Group>
          </EditorCard>
        </div>

        {/* Right column — vehicle binding sidebar */}
        <div className="space-y-6">
          <EditorCard
            title="Vehicle binding"
            icon={<Truck className="h-3.5 w-3.5" strokeWidth={2.3} />}
          >
            <p className="text-[11.5px] text-[var(--color-ink-500)]">
              Optional — tell the dispatcher which vehicle, group, or wait area
              should pick up orders created from this template.
            </p>
            <div className="mt-3 space-y-2.5">
              <FieldText
                label="Vehicle key"
                placeholder="e.g. AGV-007"
                value={form.appointVehicleKey}
                onChange={(v) => setForm({ ...form, appointVehicleKey: v })}
                mono
              />
              <FieldText
                label="Vehicle name"
                placeholder="Friendly name"
                value={form.appointVehicleName}
                onChange={(v) => setForm({ ...form, appointVehicleName: v })}
              />
              <FieldText
                label="Group key"
                placeholder="e.g. FLOOR_3"
                value={form.appointVehicleGroupKey}
                onChange={(v) => setForm({ ...form, appointVehicleGroupKey: v })}
                mono
              />
              <FieldText
                label="Group name"
                placeholder="Friendly group name"
                value={form.appointVehicleGroupName}
                onChange={(v) => setForm({ ...form, appointVehicleGroupName: v })}
              />
              <FieldText
                label="Queue wait area"
                placeholder="e.g. WAITZONE_A"
                value={form.appointQueueWaitArea}
                onChange={(v) => setForm({ ...form, appointQueueWaitArea: v })}
                mono
              />
            </div>
          </EditorCard>

          <EditorCard
            title="What gets sent to RIOT3"
            icon={<Check className="h-3.5 w-3.5" strokeWidth={2.3} />}
          >
            <ul className="space-y-1.5 text-[11.5px] text-[var(--color-ink-600)]">
              <Bullet>
                <strong>name</strong>, <strong>priority</strong>, and the resolved
                missions[] array (with ActionTemplate refs replaced by the
                catalog values at dispatch time).
              </Bullet>
              <Bullet>
                Vehicle binding hints — operators can still override at the
                dispatch dialog.
              </Bullet>
              <Bullet>
                Pickup/drop station codes are resolved to GUIDs server-side so
                payloads stay portable across environments.
              </Bullet>
            </ul>
          </EditorCard>
        </div>
      </div>
    </form>
  );
}

// ── Mission row ────────────────────────────────────────────────────────

function MissionRow({
  mission,
  index,
  total,
  onChange,
  onRemove,
  onMove,
  mapVendors,
  stationVendors,
  actionTemplates,
}: {
  mission: MissionForm;
  index: number;
  total: number;
  onChange: (patch: Partial<MissionForm>) => void;
  onRemove: () => void;
  onMove: (dir: -1 | 1) => void;
  mapVendors: { maps: MapVendorOption[]; loading: boolean; error: string | null };
  stationVendors: {
    stations: StationVendorOption[];
    loading: boolean;
    error: string | null;
  };
  actionTemplates: {
    templates: ActionTemplateOption[];
    loading: boolean;
    error: string | null;
  };
}) {
  const isMove = mission.type === "MOVE";
  const dragControls = useDragControls();
  return (
    <Reorder.Item
      value={mission}
      as="div"
      dragListener={false}
      dragControls={dragControls}
      layout
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -4, transition: { duration: 0.18 } }}
      transition={{ duration: 0.3, ease: [0.22, 1, 0.36, 1] }}
      whileDrag={{
        scale: 1.015,
        boxShadow: "0 24px 48px -16px rgba(15,23,42,0.35)",
        zIndex: 30,
      }}
      className="relative rounded-[var(--radius-sm)] border border-white/70 bg-white/55 px-3 py-2.5 backdrop-blur dark:border-white/[0.06] dark:bg-white/[0.04]"
    >
      <div className="flex items-start gap-3">
        {/* Drag handle column — pointer-down on the grip starts a reorder */}
        <div className="flex flex-col items-center gap-1 pt-1">
          <span
            className={cn(
              "grid h-7 w-7 place-items-center rounded-full text-white shadow-[0_4px_10px_-4px_rgba(15,23,42,0.45)]",
              isMove
                ? "bg-gradient-to-br from-[var(--color-pastel-mint-ink)] to-[#0e6a4d]"
                : "bg-gradient-to-br from-[var(--color-pastel-peach-ink)] to-[#a8421d]",
            )}
            title={`Mission #${index + 1}`}
          >
            {isMove ? (
              <Navigation className="h-3.5 w-3.5" strokeWidth={2.4} />
            ) : (
              <Workflow className="h-3.5 w-3.5" strokeWidth={2.4} />
            )}
          </span>
          <button
            type="button"
            onPointerDown={(e) => dragControls.start(e)}
            title="Drag to reorder"
            aria-label={`Drag mission ${index + 1} to reorder`}
            className="grid h-6 w-6 cursor-grab touch-none place-items-center rounded-md text-[var(--color-ink-400)] transition-colors hover:bg-white/55 hover:text-[var(--color-ink-700)] active:cursor-grabbing dark:hover:bg-white/[0.06]"
          >
            <GripVertical className="h-3.5 w-3.5" strokeWidth={2.2} />
          </button>
        </div>

        <div className="flex-1 space-y-2.5">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="flex items-center gap-1.5">
              <span className="font-mono text-[10px] font-bold uppercase tracking-[0.12em] text-[var(--color-ink-400)]">
                #{index + 1}
              </span>
              <TypeToggle
                value={mission.type}
                onChange={(t) =>
                  onChange({
                    type: t,
                    // Reset cross-type fields when switching
                    actionTemplateName: t === "ACT" ? mission.actionTemplateName : "",
                    actionType: t === "ACT" ? mission.actionType : "",
                    actionParameters: t === "ACT" ? mission.actionParameters : [],
                    mapId: t === "MOVE" ? mission.mapId : "",
                    stationId: t === "MOVE" ? mission.stationId : "",
                  })
                }
              />
            </div>
            <div className="flex items-center gap-1">
              <IconBtn
                onClick={() => onMove(-1)}
                disabled={index === 0}
                title="Move up"
              >
                <ArrowUp className="h-3 w-3" strokeWidth={2.4} />
              </IconBtn>
              <IconBtn
                onClick={() => onMove(1)}
                disabled={index === total - 1}
                title="Move down"
              >
                <ArrowDown className="h-3 w-3" strokeWidth={2.4} />
              </IconBtn>
              <IconBtn onClick={onRemove} title="Remove" tone="coral">
                <Trash2 className="h-3 w-3" strokeWidth={2.4} />
              </IconBtn>
            </div>
          </div>

          {isMove ? (
            <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
              <FieldCombobox
                label="Map ID"
                value={mission.mapId}
                onChange={(v) => onChange({ mapId: v })}
                options={mapVendors.maps.map<IntIdOption>((m) => ({
                  vendorId: m.vendorId,
                  label: m.name,
                  secondary: m.version ? `v${m.version}` : null,
                }))}
                loading={mapVendors.loading}
                error={mapVendors.error}
                placeholder="Select map…"
                loadingPlaceholder="Loading maps…"
                emptyLabel="No map matches"
                notInListLabel={(v) => `Map id ${v} isn't in the list`}
                icon={<MapIcon className="h-3 w-3" />}
              />
              <FieldCombobox
                label="Station ID"
                value={mission.stationId}
                onChange={(v) => onChange({ stationId: v })}
                options={stationVendors.stations.map<IntIdOption>((s) => ({
                  vendorId: s.vendorId,
                  label: s.name,
                  secondary: s.code,
                  badge: s.type,
                }))}
                loading={stationVendors.loading}
                error={stationVendors.error}
                placeholder="Select station…"
                loadingPlaceholder="Loading stations…"
                emptyLabel="No station matches"
                notInListLabel={(v) => `Station id ${v} isn't in the list`}
                icon={<Tag className="h-3 w-3" />}
              />
            </div>
          ) : (
            <div className="space-y-2.5">
              <div className="flex items-center gap-1 rounded-full bg-white/45 p-1 text-[10.5px] font-bold uppercase tracking-[0.08em] dark:bg-white/[0.04]">
                {(["reference", "inline"] as const).map((v) => {
                  const active = mission.actionVariant === v;
                  return (
                    <button
                      key={v}
                      type="button"
                      onClick={() => onChange({ actionVariant: v })}
                      className={cn(
                        "cursor-pointer rounded-full px-3 py-1 transition-all",
                        active
                          ? "bg-[var(--color-brand-900)] text-white dark:bg-[var(--color-brand-500)]"
                          : "text-[var(--color-ink-600)] hover:text-[var(--color-ink-900)]",
                      )}
                    >
                      {v === "reference"
                        ? "Action template reference"
                        : "Inline action"}
                    </button>
                  );
                })}
              </div>
              {mission.actionVariant === "reference" ? (
                <label className="block">
                  <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
                    Action template name
                  </span>
                  <div className="mt-1">
                    <ActionTemplateCombobox
                      value={mission.actionTemplateName}
                      onChange={(v) => onChange({ actionTemplateName: v })}
                      templates={actionTemplates.templates}
                      loading={actionTemplates.loading}
                      error={actionTemplates.error}
                      placeholder="Select action template…"
                      icon={<Workflow className="h-3 w-3" />}
                    />
                  </div>
                </label>
              ) : (
                <div className="space-y-2.5">
                  <FieldText
                    label="Action type"
                    placeholder="e.g. standardRobotsCustom"
                    value={mission.actionType}
                    onChange={(v) => onChange({ actionType: v })}
                    mono
                    compact
                  />
                  <ParameterEditor
                    parameters={mission.actionParameters}
                    onChange={(actionParameters) => onChange({ actionParameters })}
                  />
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </Reorder.Item>
  );
}

function ParameterEditor({
  parameters,
  onChange,
}: {
  parameters: { key: string; value: string }[];
  onChange: (next: { key: string; value: string }[]) => void;
}) {
  function update(i: number, patch: Partial<{ key: string; value: string }>) {
    onChange(parameters.map((p, idx) => (idx === i ? { ...p, ...patch } : p)));
  }
  function add() {
    onChange([...parameters, { key: "", value: "" }]);
  }
  function remove(i: number) {
    onChange(parameters.filter((_, idx) => idx !== i));
  }
  return (
    <div>
      <div className="mb-1.5 flex items-center justify-between">
        <span className="text-[10px] font-semibold uppercase tracking-[0.12em] text-[var(--color-ink-500)]">
          Parameters
        </span>
        <button
          type="button"
          onClick={add}
          className="inline-flex cursor-pointer items-center gap-1 rounded-full bg-white/55 px-2 py-0.5 text-[10.5px] font-semibold text-[var(--color-ink-700)] hover:bg-white/80 dark:bg-white/[0.04] dark:hover:bg-white/[0.08]"
        >
          <Plus className="h-3 w-3" strokeWidth={2.4} />
          Add
        </button>
      </div>
      <div className="space-y-1.5">
        {parameters.map((p, i) => (
          <div key={i} className="grid grid-cols-[1fr_1fr_auto] gap-1.5">
            <input
              type="text"
              value={p.key}
              onChange={(e) => update(i, { key: e.target.value })}
              placeholder="key"
              className="rounded-[8px] border border-white/70 bg-white/65 px-2 py-1 font-mono text-[11px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] focus:border-[var(--color-brand-500)]/40 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.04]"
            />
            <input
              type="text"
              value={p.value}
              onChange={(e) => update(i, { value: e.target.value })}
              placeholder="value"
              className="rounded-[8px] border border-white/70 bg-white/65 px-2 py-1 font-mono text-[11px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] focus:border-[var(--color-brand-500)]/40 focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.04]"
            />
            <button
              type="button"
              onClick={() => remove(i)}
              className="cursor-pointer rounded-md p-1.5 text-[var(--color-ink-500)] transition-colors hover:bg-[var(--color-coral)]/15 hover:text-[var(--color-coral)]"
              aria-label="Remove parameter"
            >
              <X className="h-3 w-3" strokeWidth={2.4} />
            </button>
          </div>
        ))}
        {parameters.length === 0 && (
          <div className="rounded-[8px] border border-dashed border-[var(--color-ink-200)] px-2 py-2 text-center text-[10.5px] italic text-[var(--color-ink-400)] dark:border-white/[0.08]">
            No parameters
          </div>
        )}
      </div>
    </div>
  );
}

// ── Bits ───────────────────────────────────────────────────────────────

function EditorCard({
  title,
  icon,
  action,
  children,
}: {
  title: string;
  icon: React.ReactNode;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <motion.section
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
      className="rounded-[var(--radius-xl)] glass p-5 sm:p-6"
    >
      <div className="mb-4 flex items-center justify-between gap-2">
        <h2 className="inline-flex items-center gap-2 text-[12.5px] font-bold uppercase tracking-[0.12em] text-[var(--color-ink-700)]">
          <span className="text-[var(--color-brand-500)]">{icon}</span>
          {title}
        </h2>
        {action}
      </div>
      {children}
    </motion.section>
  );
}

function FieldText({
  label,
  value,
  onChange,
  placeholder,
  required,
  helper,
  icon,
  mono = false,
  compact = false,
  disabled,
  className,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  required?: boolean;
  helper?: string;
  icon?: React.ReactNode;
  mono?: boolean;
  compact?: boolean;
  disabled?: boolean;
  className?: string;
}) {
  return (
    <label className={cn("block", className)}>
      <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
        {required && <span className="ml-0.5 text-[var(--color-coral)]">*</span>}
      </span>
      <div className="relative mt-1">
        {icon && (
          <span className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-ink-400)]">
            {icon}
          </span>
        )}
        <input
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          required={required}
          disabled={disabled}
          className={cn(
            "w-full rounded-[var(--radius-sm)] border border-white/70 bg-white/65 text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] backdrop-blur transition-colors",
            "focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20",
            "disabled:cursor-not-allowed disabled:opacity-60",
            "dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]",
            compact ? "px-2.5 py-1.5 text-[12px]" : "px-3 py-2 text-[12.5px]",
            mono && "font-mono",
            icon && (compact ? "pl-7" : "pl-8"),
          )}
        />
      </div>
      {helper && (
        <span className="mt-1 block text-[10.5px] text-[var(--color-ink-400)]">
          {helper}
        </span>
      )}
    </label>
  );
}

// FieldCombobox — wraps IntIdCombobox in the same label shell as
// FieldText so the MOVE row's three columns line up visually.
function FieldCombobox({
  label,
  value,
  onChange,
  options,
  loading,
  error,
  placeholder,
  loadingPlaceholder,
  emptyLabel,
  notInListLabel,
  icon,
  className,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: IntIdOption[];
  loading?: boolean;
  error?: string | null;
  placeholder?: string;
  loadingPlaceholder?: string;
  emptyLabel?: string;
  notInListLabel?: (value: string) => string;
  icon?: React.ReactNode;
  className?: string;
}) {
  return (
    <label className={cn("block", className)}>
      <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
      </span>
      <div className="mt-1">
        <IntIdCombobox
          value={value}
          onChange={onChange}
          options={options}
          loading={loading}
          error={error}
          placeholder={placeholder}
          loadingPlaceholder={loadingPlaceholder}
          emptyLabel={emptyLabel}
          notInListLabel={notInListLabel}
          icon={icon}
        />
      </div>
    </label>
  );
}

function FieldNumber({
  label,
  value,
  onChange,
  placeholder,
  required,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  required?: boolean;
}) {
  return (
    <label className="block">
      <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
        {required && <span className="ml-0.5 text-[var(--color-coral)]">*</span>}
      </span>
      <input
        type="number"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        required={required}
        className="mt-1 w-full rounded-[var(--radius-sm)] border border-white/70 bg-white/65 px-3 py-2 font-mono text-[12.5px] text-[var(--color-ink-900)] placeholder:text-[var(--color-ink-400)] backdrop-blur focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]"
      />
    </label>
  );
}

function FieldStation({
  label,
  value,
  onChange,
  placeholder,
  stations,
  loading,
  error,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  stations: StationOption[];
  loading: boolean;
  error: string | null;
}) {
  return (
    <label className="block">
      <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
      </span>
      <div className="mt-1">
        <StationCombobox
          value={value}
          onChange={onChange}
          placeholder={placeholder}
          stations={stations}
          loading={loading}
          error={error}
        />
      </div>
    </label>
  );
}

function FieldSelect({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
}) {
  return (
    <label className="block">
      <span className="block text-[10.5px] font-semibold uppercase tracking-[0.1em] text-[var(--color-ink-500)]">
        {label}
      </span>
      <div className="relative mt-1">
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          className="w-full cursor-pointer appearance-none rounded-[var(--radius-sm)] border border-white/70 bg-white/65 px-3 py-2 pr-8 text-[12.5px] text-[var(--color-ink-900)] backdrop-blur focus:border-[var(--color-brand-500)]/40 focus:bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-brand-500)]/20 dark:border-white/[0.06] dark:bg-white/[0.04] dark:focus:bg-white/[0.08]"
        >
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <ChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--color-ink-400)]" />
      </div>
    </label>
  );
}

function TypeToggle({
  value,
  onChange,
}: {
  value: MissionType;
  onChange: (v: MissionType) => void;
}) {
  return (
    <div className="inline-flex items-center gap-0.5 rounded-full bg-white/55 p-0.5 text-[10px] font-bold uppercase tracking-[0.1em] dark:bg-white/[0.04]">
      {(["MOVE", "ACT"] as const).map((t) => {
        const active = value === t;
        const isMove = t === "MOVE";
        return (
          <button
            key={t}
            type="button"
            onClick={() => onChange(t)}
            className={cn(
              "cursor-pointer rounded-full px-2 py-0.5 transition-all",
              active
                ? isMove
                  ? "bg-[var(--color-pastel-mint-ink)] text-white shadow-[0_2px_6px_-2px_rgba(0,0,0,0.3)]"
                  : "bg-[var(--color-pastel-peach-ink)] text-white shadow-[0_2px_6px_-2px_rgba(0,0,0,0.3)]"
                : "text-[var(--color-ink-500)] hover:text-[var(--color-ink-800)]",
            )}
          >
            {t}
          </button>
        );
      })}
    </div>
  );
}

function IconBtn({
  onClick,
  disabled,
  title,
  tone = "default",
  children,
}: {
  onClick: () => void;
  disabled?: boolean;
  title: string;
  tone?: "default" | "coral";
  children: React.ReactNode;
}) {
  const tones = {
    default:
      "text-[var(--color-ink-500)] hover:bg-white/55 hover:text-[var(--color-ink-900)] dark:hover:bg-white/[0.06]",
    coral:
      "text-[var(--color-ink-500)] hover:bg-[var(--color-coral)]/15 hover:text-[var(--color-coral)]",
  };
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={cn(
        "grid h-6 w-6 cursor-pointer place-items-center rounded-md transition-colors disabled:cursor-not-allowed disabled:opacity-30",
        tones[tone],
      )}
    >
      {children}
    </button>
  );
}

function CountPill({ tone, label }: { tone: "mint" | "peach"; label: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-bold tracking-[0.08em]",
        tone === "mint"
          ? "bg-[var(--color-pastel-mint)] text-[var(--color-pastel-mint-ink)]"
          : "bg-[var(--color-pastel-peach)] text-[var(--color-pastel-peach-ink)]",
      )}
    >
      {label}
    </span>
  );
}

function AddMissionBtn({
  type,
  onAdd,
}: {
  type: MissionType;
  onAdd: () => void;
}) {
  const isMove = type === "MOVE";
  return (
    <motion.button
      type="button"
      whileTap={{ scale: 0.96 }}
      whileHover={{ y: -1 }}
      onClick={onAdd}
      className={cn(
        "inline-flex cursor-pointer items-center gap-1 rounded-full px-2.5 py-1 text-[10.5px] font-bold uppercase tracking-[0.08em] shadow-[0_4px_12px_-4px_rgba(15,23,42,0.35)]",
        isMove
          ? "bg-[var(--color-pastel-mint-ink)] text-white"
          : "bg-[var(--color-pastel-peach-ink)] text-white",
      )}
    >
      <Plus className="h-3 w-3" strokeWidth={2.4} />
      Add {type}
    </motion.button>
  );
}

function Bullet({ children }: { children: React.ReactNode }) {
  return (
    <li className="flex items-start gap-1.5">
      <span className="mt-1 inline-block h-1 w-1 shrink-0 rounded-full bg-[var(--color-brand-500)]" />
      <span>{children}</span>
    </li>
  );
}
