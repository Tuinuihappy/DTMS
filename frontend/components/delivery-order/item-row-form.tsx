"use client";

import { useState } from "react";
import { ChevronDown, ChevronUp, Trash2 } from "lucide-react";
import {
  useFormContext,
  type UseFieldArrayReturn,
} from "react-hook-form";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

import {
  HANDLING_VALUES,
  PACKING_GROUP_VALUES,
  UOM_VALUES,
  type CreateDraftDeliveryOrderFormValues,
} from "@/types/delivery-order";
import { cn } from "@/lib/utils";

interface ItemRowFormProps {
  index: number;
  fieldArray: UseFieldArrayReturn<CreateDraftDeliveryOrderFormValues, "items">;
}

// One row inside the items field-array. Primary fields are always
// visible; dimensions / hazmat / temperature / handling collapse under
// an "Advanced" disclosure so the form stays scannable when most items
// are simple (just itemId + pickup + drop + quantity).
export function ItemRowForm({ index, fieldArray }: ItemRowFormProps) {
  const { control } = useFormContext<CreateDraftDeliveryOrderFormValues>();
  const [showAdvanced, setShowAdvanced] = useState(false);

  return (
    <div className="liquid-glass-subtle relative space-y-3 rounded-2xl p-4">
      <div className="relative z-[2] flex items-center justify-between">
        <span className="text-[11px] font-semibold uppercase tracking-[0.06em] text-muted-foreground">
          Item #{index + 1}
        </span>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          onClick={() => fieldArray.remove(index)}
          aria-label="Remove item"
          className="liquid-puck size-7 rounded-full text-destructive hover:bg-transparent"
        >
          <Trash2 className="relative z-[2] h-3 w-3" strokeWidth={2.25} />
        </Button>
      </div>

      <div className="relative z-[2] grid grid-cols-1 gap-3 sm:grid-cols-2">
        <FormField
          control={control}
          name={`items.${index}.itemId`}
          render={({ field }) => (
            <FormItem className="sm:col-span-2">
              <FormLabel>itemId</FormLabel>
              <FormControl>
                <Input
                  {...field}
                  placeholder="BOX-001"
                  autoComplete="off"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name={`items.${index}.pickupLocationCode`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Pickup code</FormLabel>
              <FormControl>
                <Input {...field} placeholder="WAREHOUSE_A" />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`items.${index}.dropLocationCode`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Drop code</FormLabel>
              <FormControl>
                <Input {...field} placeholder="DOCK_B" />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name={`items.${index}.quantityValue`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Quantity</FormLabel>
              <FormControl>
                <Input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="any"
                  {...field}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`items.${index}.quantityUom`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>UoM</FormLabel>
              <Select onValueChange={field.onChange} value={field.value}>
                <FormControl>
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                </FormControl>
                <SelectContent>
                  {UOM_VALUES.map((u) => (
                    <SelectItem key={u} value={u}>
                      {u}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name={`items.${index}.weightKg`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Weight (kg)</FormLabel>
              <FormControl>
                <Input
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="any"
                  placeholder="optional"
                  {...field}
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={control}
          name={`items.${index}.loadUnitProfileCode`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Load unit profile</FormLabel>
              <FormControl>
                <Input {...field} placeholder="PALLET / TOTE" />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={control}
          name={`items.${index}.description`}
          render={({ field }) => (
            <FormItem className="sm:col-span-2">
              <FormLabel>Description (optional)</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>

      <button
        type="button"
        onClick={() => setShowAdvanced((v) => !v)}
        className="press-feedback relative z-[2] flex items-center gap-1 text-[11px] font-medium text-primary"
      >
        {showAdvanced ? (
          <ChevronUp className="h-3 w-3" strokeWidth={2.25} />
        ) : (
          <ChevronDown className="h-3 w-3" strokeWidth={2.25} />
        )}
        {showAdvanced ? "Hide advanced" : "Show advanced (dims · hazmat · temp · handling)"}
      </button>

      <div
        className={cn(
          "relative z-[2] space-y-3 border-l-2 border-primary/30 pl-4",
          showAdvanced ? "block" : "hidden"
        )}
      >
        <div className="grid grid-cols-3 gap-3">
          <FormField
            control={control}
            name={`items.${index}.lengthMm`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>L (mm)</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`items.${index}.widthMm`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>W (mm)</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`items.${index}.heightMm`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>H (mm)</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <FormField
            control={control}
            name={`items.${index}.hazmatClass`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Hazmat class</FormLabel>
                <FormControl>
                  <Input {...field} placeholder="e.g. 3 or 2.1" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`items.${index}.hazmatPackingGroup`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Packing group</FormLabel>
                <Select
                  onValueChange={(v) => field.onChange(v === "__none__" ? undefined : v)}
                  value={field.value ?? "__none__"}
                >
                  <FormControl>
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    <SelectItem value="__none__">—</SelectItem>
                    {PACKING_GROUP_VALUES.map((g) => (
                      <SelectItem key={g} value={g}>
                        {g}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <FormField
            control={control}
            name={`items.${index}.temperatureMinC`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Min °C</FormLabel>
                <FormControl>
                  <Input
                    type="number"
                    inputMode="decimal"
                    step="any"
                    {...field}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`items.${index}.temperatureMaxC`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Max °C</FormLabel>
                <FormControl>
                  <Input
                    type="number"
                    inputMode="decimal"
                    step="any"
                    {...field}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>

        <FormField
          control={control}
          name={`items.${index}.handling`}
          render={({ field }) => (
            <FormItem>
              <FormLabel>Handling</FormLabel>
              <div className="flex flex-wrap gap-2">
                {HANDLING_VALUES.map((h) => {
                  const checked = (field.value ?? []).includes(h);
                  return (
                    <label
                      key={h}
                      className={cn(
                        "press-feedback flex cursor-pointer items-center gap-1.5 rounded-full px-3 py-1 text-[11px] font-medium transition-colors",
                        checked
                          ? "bg-primary/15 text-primary"
                          : "bg-black/[0.04] text-muted-foreground hover:bg-black/[0.07] dark:bg-white/[0.06] dark:hover:bg-white/[0.10]"
                      )}
                    >
                      <Checkbox
                        checked={checked}
                        onCheckedChange={(c) => {
                          const set = new Set(field.value ?? []);
                          if (c) set.add(h);
                          else set.delete(h);
                          field.onChange(Array.from(set));
                        }}
                        className="h-3 w-3"
                      />
                      {h}
                    </label>
                  );
                })}
              </div>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>
    </div>
  );
}
