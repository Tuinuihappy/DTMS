"use client";

import { ChevronDown, ChevronUp, Trash2 } from "lucide-react";
import {
  useFormContext,
  useWatch,
  type UseFieldArrayReturn,
} from "react-hook-form";

import type { CreateOrderTemplateFormValues } from "@/types/order-template";
import type { ActionTemplateDto } from "@/types/action-template";

import { Button } from "@/components/ui/button";
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

interface MissionRowProps {
  index: number;
  fieldArray: UseFieldArrayReturn<CreateOrderTemplateFormValues, "missions">;
  templates: ActionTemplateDto[];
  templatesLoading: boolean;
}

// Single mission inside the OrderTemplate composer. The row collapses
// between MOVE inputs and ACT inputs based on the type select, mirroring
// the parser's contract on the backend (MOVE needs mapId+stationId; ACT
// needs either a template ref OR inline params).
export function MissionRow({
  index,
  fieldArray,
  templates,
  templatesLoading,
}: MissionRowProps) {
  const { control } = useFormContext<CreateOrderTemplateFormValues>();
  const type = useWatch({ control, name: `missions.${index}.type` });
  const total = fieldArray.fields.length;

  return (
    <div className="liquid-glass-subtle space-y-4 rounded-2xl p-4">
      <div className="flex items-center justify-between gap-2">
        <span className="text-xs font-medium text-muted-foreground">
          Mission #{index + 1}
        </span>
        <div className="flex items-center gap-0.5">
          <Button
            type="button"
            size="icon"
            variant="ghost"
            disabled={index === 0}
            onClick={() => fieldArray.move(index, index - 1)}
            aria-label="Move up"
          >
            <ChevronUp className="h-3.5 w-3.5" />
          </Button>
          <Button
            type="button"
            size="icon"
            variant="ghost"
            disabled={index === total - 1}
            onClick={() => fieldArray.move(index, index + 1)}
            aria-label="Move down"
          >
            <ChevronDown className="h-3.5 w-3.5" />
          </Button>
          <Button
            type="button"
            size="icon"
            variant="ghost"
            onClick={() => fieldArray.remove(index)}
            aria-label="Remove mission"
            className="text-destructive hover:text-destructive"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      </div>

      <FormField
        control={control}
        name={`missions.${index}.type`}
        render={({ field }) => (
          <FormItem>
            <FormLabel>Type</FormLabel>
            <Select onValueChange={field.onChange} value={field.value}>
              <FormControl>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
              </FormControl>
              <SelectContent>
                <SelectItem value="MOVE">MOVE — go to station</SelectItem>
                <SelectItem value="ACT">ACT — perform action</SelectItem>
              </SelectContent>
            </Select>
            <FormMessage />
          </FormItem>
        )}
      />

      {type === "MOVE" ? (
        <div className="grid grid-cols-2 gap-3">
          <FormField
            control={control}
            name={`missions.${index}.mapId`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>mapId (int)</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={control}
            name={`missions.${index}.stationId`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>stationId (int)</FormLabel>
                <FormControl>
                  <Input type="number" inputMode="numeric" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </div>
      ) : (
        <ActFields index={index} templates={templates} loading={templatesLoading} />
      )}
    </div>
  );
}

function ActFields({
  index,
  templates,
  loading,
}: {
  index: number;
  templates: ActionTemplateDto[];
  loading: boolean;
}) {
  const { control, setValue } = useFormContext<CreateOrderTemplateFormValues>();
  const templateName = useWatch({
    control,
    name: `missions.${index}.actionTemplateName`,
  });
  const inlineActionType = useWatch({
    control,
    name: `missions.${index}.actionType`,
  });
  const useInline = !templateName?.trim();

  return (
    <div className="space-y-3">
      <FormField
        control={control}
        name={`missions.${index}.actionTemplateName`}
        render={({ field }) => (
          <FormItem>
            <FormLabel>ActionTemplate</FormLabel>
            <Select
              onValueChange={(v) => {
                if (v === "__none__") {
                  field.onChange("");
                } else {
                  field.onChange(v);
                  // Picking a template clears any inline values so the
                  // backend's XOR rule doesn't reject us.
                  setValue(`missions.${index}.actionType`, "");
                  setValue(`missions.${index}.inlineActionId`, "");
                  setValue(`missions.${index}.inlineParam0`, "");
                  setValue(`missions.${index}.inlineParam1`, "");
                  setValue(`missions.${index}.inlineParamStr`, "");
                }
              }}
              value={field.value || "__none__"}
              disabled={loading}
            >
              <FormControl>
                <SelectTrigger>
                  <SelectValue placeholder="Pick a template or use inline params" />
                </SelectTrigger>
              </FormControl>
              <SelectContent>
                <SelectItem value="__none__">— inline params —</SelectItem>
                {templates
                  .filter((t) => t.isActive)
                  .map((t) => (
                    <SelectItem key={t.id} value={t.name}>
                      {t.name}
                      <span className="ml-2 text-xs text-muted-foreground">
                        {t.actionType}
                      </span>
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
            <FormMessage />
          </FormItem>
        )}
      />

      {useInline ? (
        <div className="space-y-3 rounded-r-md border-l-2 border-primary/30 pl-4">
          <FormField
            control={control}
            name={`missions.${index}.actionType`}
            render={({ field }) => (
              <FormItem>
                <FormLabel>Inline actionType</FormLabel>
                <FormControl>
                  <Input {...field} placeholder="standardRobotsCustom" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          {inlineActionType ? (
            <>
              <div className="grid grid-cols-3 gap-3">
                <FormField
                  control={control}
                  name={`missions.${index}.inlineActionId`}
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>id</FormLabel>
                      <FormControl>
                        <Input type="number" inputMode="numeric" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`missions.${index}.inlineParam0`}
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>param0</FormLabel>
                      <FormControl>
                        <Input type="number" inputMode="numeric" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={control}
                  name={`missions.${index}.inlineParam1`}
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>param1</FormLabel>
                      <FormControl>
                        <Input type="number" inputMode="numeric" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>
              <FormField
                control={control}
                name={`missions.${index}.inlineParamStr`}
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>param_str (optional)</FormLabel>
                    <FormControl>
                      <Input {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
            </>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
