"use client";

import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

import { orderTemplatesApi, type InstantiateResult } from "@/lib/order-templates";
import { ApiError } from "@/lib/api";
import type { OrderTemplateDto } from "@/types/order-template";

import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { JsonViewer } from "@/components/shared/json-viewer";

interface InstantiateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  template: OrderTemplateDto | null;
}

interface OverrideForm {
  priority: string;
  appointVehicleKey: string;
  appointVehicleName: string;
  appointVehicleGroupKey: string;
  appointVehicleGroupName: string;
  appointQueueWaitArea: string;
  upperKey: string;
  dryRun: boolean;
}

const initialForm: OverrideForm = {
  priority: "",
  appointVehicleKey: "",
  appointVehicleName: "",
  appointVehicleGroupKey: "",
  appointVehicleGroupName: "",
  appointQueueWaitArea: "",
  upperKey: "",
  dryRun: true,
};

export function InstantiateDialog({
  open,
  onOpenChange,
  template,
}: InstantiateDialogProps) {
  const [form, setForm] = useState<OverrideForm>(initialForm);
  const [result, setResult] = useState<InstantiateResult | null>(null);

  // Reset every time the dialog reopens so previous previews don't bleed
  // into a fresh attempt.
  useEffect(() => {
    if (open) {
      setForm(initialForm);
      setResult(null);
    }
  }, [open]);

  const mutation = useMutation({
    mutationFn: async () => {
      if (!template) throw new Error("No template selected");
      const body = {
        priority: form.priority ? Number(form.priority) : undefined,
        appointVehicleKey: form.appointVehicleKey || undefined,
        appointVehicleName: form.appointVehicleName || undefined,
        appointVehicleGroupKey: form.appointVehicleGroupKey || undefined,
        appointVehicleGroupName: form.appointVehicleGroupName || undefined,
        appointQueueWaitArea: form.appointQueueWaitArea || undefined,
        upperKey: form.upperKey || undefined,
        dryRun: form.dryRun,
      };
      return orderTemplatesApi.instantiate(template.id, body);
    },
    onSuccess: (data) => {
      setResult(data);
      if (form.dryRun) {
        toast.success("Dry run resolved successfully");
      } else {
        toast.success(
          data.riotOrderKey
            ? `RIOT3 accepted order ${data.riotOrderKey}`
            : "Order submitted to RIOT3"
        );
      }
    },
    onError: (error: unknown) => {
      const message =
        error instanceof ApiError ? error.message : "Instantiate failed";
      toast.error(message);
    },
  });

  function field<K extends keyof OverrideForm>(key: K, value: OverrideForm[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  if (!template) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Instantiate {template.name}</DialogTitle>
          <DialogDescription>
            Resolve ActionTemplate refs and submit to RIOT3. Use dry-run to
            preview the resolved envelope without contacting the robot.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <Labeled label="priority override">
              <Input
                type="number"
                inputMode="numeric"
                placeholder={String(template.priority)}
                value={form.priority}
                onChange={(e) => field("priority", e.target.value)}
              />
            </Labeled>
            <Labeled label="upperKey override">
              <Input
                value={form.upperKey}
                onChange={(e) => field("upperKey", e.target.value)}
                placeholder="(server picks one if blank)"
              />
            </Labeled>

            <Labeled label="appointVehicleKey">
              <Input
                value={form.appointVehicleKey}
                onChange={(e) => field("appointVehicleKey", e.target.value)}
              />
            </Labeled>
            <Labeled label="appointVehicleName">
              <Input
                value={form.appointVehicleName}
                onChange={(e) => field("appointVehicleName", e.target.value)}
              />
            </Labeled>

            <Labeled label="appointVehicleGroupKey">
              <Input
                value={form.appointVehicleGroupKey}
                onChange={(e) => field("appointVehicleGroupKey", e.target.value)}
              />
            </Labeled>
            <Labeled label="appointVehicleGroupName">
              <Input
                value={form.appointVehicleGroupName}
                onChange={(e) =>
                  field("appointVehicleGroupName", e.target.value)
                }
              />
            </Labeled>

            <Labeled label="appointQueueWaitArea" className="col-span-2">
              <Input
                value={form.appointQueueWaitArea}
                onChange={(e) => field("appointQueueWaitArea", e.target.value)}
              />
            </Labeled>
          </div>

          <label className="flex items-center justify-between rounded-md border p-3">
            <div>
              <p className="text-sm font-medium">Dry run</p>
              <p className="text-xs text-muted-foreground">
                Resolve and return the envelope without calling RIOT3.
              </p>
            </div>
            <Switch
              checked={form.dryRun}
              onCheckedChange={(v) => field("dryRun", Boolean(v))}
            />
          </label>

          {result ? (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <p className="text-xs font-medium uppercase text-muted-foreground">
                  Resolved envelope
                </p>
                {result.riotOrderKey ? (
                  <p className="text-xs">
                    RIOT3 orderKey:{" "}
                    <code className="font-mono">{result.riotOrderKey}</code>
                  </p>
                ) : null}
              </div>
              <JsonViewer value={result.resolvedEnvelope} />
            </div>
          ) : null}
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={mutation.isPending}
          >
            Close
          </Button>
          <Button
            onClick={() => mutation.mutate()}
            disabled={mutation.isPending}
          >
            {mutation.isPending
              ? "Working…"
              : form.dryRun
              ? "Preview"
              : "Submit to RIOT3"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function Labeled({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className={className}>
      <Label className="mb-1 block text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </Label>
      {children}
    </div>
  );
}
