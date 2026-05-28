using AMR.DeliveryPlanning.Planning.Application.Commands.AssignVehicleToJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.ConsolidateOrders;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateCrossDockJobs;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateMilkRun;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateMultiPickDropJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateOrderTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.DeleteActionTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.DeleteOrderTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.InstantiateOrderTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.ReplanJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.SetActionTemplateActive;
using AMR.DeliveryPlanning.Planning.Application.Commands.SetOrderTemplateActive;
using AMR.DeliveryPlanning.Planning.Application.Commands.UpdateActionTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.UpdateCostModel;
using AMR.DeliveryPlanning.Planning.Application.Commands.UpdateOrderTemplate;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplateById;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetCostModel;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetOrderTemplateById;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetOrderTemplates;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetPendingJobs;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Planning.Presentation;

public static class PlanningEndpoints
{
    public static void MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/planning/jobs").WithTags("Planning").RequireAuthorization();

        // POST /api/v1/planning/jobs — Create a Job from a DeliveryOrder
        group.MapPost("/", async (CreateJobFromOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/jobs/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/v1/planning/jobs/{id}/assign — Assign a vehicle (Greedy)
        group.MapPost("/{id:guid}/assign", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new AssignVehicleToJobCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/v1/planning/jobs/{id}/commit — Commit the plan
        group.MapPost("/{id:guid}/commit", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new CommitPlanCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/v1/planning/jobs/{id}/replan — Replan a committed job
        group.MapPost("/{id:guid}/replan", async (Guid id, ReplanJobCommand command, ISender sender) =>
        {
            if (id != command.JobId) return Results.BadRequest("ID mismatch");
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // GET /api/v1/planning/jobs/{id} — Get job details
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetJobByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/v1/planning/jobs/pending — Get all pending jobs
        group.MapGet("/pending", async (ISender sender) =>
        {
            var result = await sender.Send(new GetPendingJobsQuery());
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // Consolidation + Phase 3 endpoints under /api/planning
        var planningGroup = app.MapGroup("/api/planning").WithTags("Planning").RequireAuthorization();

        // POST /api/planning/consolidate — Consolidate multiple orders into 1 job
        planningGroup.MapPost("/consolidate", async (ConsolidateOrdersCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/jobs/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/planning/cross-dock — Create linked inbound/outbound cross-dock jobs
        planningGroup.MapPost("/cross-dock", async (CreateCrossDockJobsCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/jobs/{result.Value.InboundJobId}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/planning/milk-runs — Create a milk-run template + initial job
        planningGroup.MapPost("/milk-runs", async (CreateMilkRunCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/planning/milk-runs/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/planning/multi-pick-drop — Create a CVRPPD job with pickup-delivery pairs
        planningGroup.MapPost("/multi-pick-drop", async (CreateMultiPickDropJobCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/jobs/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // GET /api/planning/cost-model — Get current cost model config
        planningGroup.MapGet("/cost-model", async (string? vehicleTypeKey, ISender sender) =>
        {
            var result = await sender.Send(new GetCostModelQuery(vehicleTypeKey));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // PUT /api/planning/cost-model — Update cost model config
        planningGroup.MapPut("/cost-model", async (UpdateCostModelCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // ── ActionTemplate catalog (Phase 1B) ────────────────────────────────
        // Mirrors RIOT3's /api/v4/order/action-templates — each entry is a
        // named recipe for one ACT mission (actionType + vendorActionId +
        // param0 + param1 + optional param_str). OrderTemplate (Phase 1C)
        // will reference these by Name.
        var actionTemplates = app.MapGroup("/api/v1/planning/action-templates")
            .WithTags("Planning")
            .RequireAuthorization();

        // POST — create a new template. Body mirrors the RIOT3
        // /api/v4/order/action-templates payload shape:
        //   { actionName, actionType, actionParameters:[{key,value}...] }
        // so operators can paste a RIOT3 example straight in.
        actionTemplates.MapPost("/", async (CreateActionTemplateRequest req, ISender sender) =>
        {
            var actionType = string.IsNullOrWhiteSpace(req.ActionType) ? "STD" : req.ActionType;

            ActionTemplateParameterSet parsed;
            try
            {
                parsed = ActionParameterParser.Parse(req.ActionParameters);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            var result = await sender.Send(new CreateActionTemplateCommand(
                Name: req.ActionName,
                VendorActionId: parsed.Id,
                Param0: parsed.Param0,
                Param1: parsed.Param1,
                ActionType: actionType,
                ParamStr: parsed.ParamStr,
                Description: req.Description));
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/action-templates/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // GET — list templates (filter by ActionType, optionally include inactive)
        // bool query params must be nullable in minimal APIs so the caller
        // can omit them — otherwise the framework returns 400.
        actionTemplates.MapGet("/", async (bool? includeInactive, string? actionType, ISender sender) =>
        {
            var result = await sender.Send(new GetActionTemplatesQuery(includeInactive ?? false, actionType));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // GET /{id} — fetch one
        actionTemplates.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetActionTemplateByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // PATCH /{id} — update the action params + meta (rename is separate).
        // Body uses the same RIOT3 shape as POST.
        actionTemplates.MapMethods("/{id:guid}", ["PATCH"],
            async (Guid id, UpdateActionTemplateRequest req, ISender sender) =>
            {
                var actionType = string.IsNullOrWhiteSpace(req.ActionType) ? "STD" : req.ActionType;

                ActionTemplateParameterSet parsed;
                try
                {
                    parsed = ActionParameterParser.Parse(req.ActionParameters);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }

                var result = await sender.Send(new UpdateActionTemplateCommand(
                    id, actionType, parsed.Id, parsed.Param0, parsed.Param1, parsed.ParamStr, req.Description));
                return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
            });

        // POST /{id}/activate, /deactivate — soft enable/disable
        actionTemplates.MapPost("/{id:guid}/activate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SetActionTemplateActiveCommand(id, true));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        actionTemplates.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SetActionTemplateActiveCommand(id, false));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // DELETE /{id} — hard delete (no OrderTemplate ref check yet — Phase 1C)
        actionTemplates.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteActionTemplateCommand(id));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // ── OrderTemplate (Phase 1C) ─────────────────────────────────────────
        // Mirrors RIOT3 /api/v4/order/order-templates payload — name +
        // priority + transportOrder{structureType,priority,missions[]} +
        // vehicle binding hints. Missions array can mix RIOT3-style inline
        // ACT missions and DTMS extensions that reference ActionTemplate
        // entries by name (handler validates the names exist).
        var orderTemplates = app.MapGroup("/api/v1/planning/order-templates")
            .WithTags("Planning")
            .RequireAuthorization();

        // POST — create a new order template
        orderTemplates.MapPost("/", async (CreateOrderTemplateRequest req, ISender sender) =>
        {
            IReadOnlyList<OrderTemplateMission> missions;
            try
            {
                missions = OrderTemplateMissionParser.ParseAll(req.TransportOrder?.Missions);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            var transport = req.TransportOrder!;
            var result = await sender.Send(new CreateOrderTemplateCommand(
                Name: req.Name,
                Priority: req.Priority,
                StructureType: string.IsNullOrWhiteSpace(transport.StructureType) ? "sequence" : transport.StructureType,
                TransportOrderPriority: transport.Priority ?? req.Priority,
                Missions: missions,
                AppointVehicleKey: req.AppointVehicleKey,
                AppointVehicleName: req.AppointVehicleName,
                AppointVehicleGroupKey: req.AppointVehicleGroupKey,
                AppointVehicleGroupName: req.AppointVehicleGroupName,
                AppointQueueWaitArea: req.AppointQueueWaitArea,
                Description: req.Description));
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/order-templates/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // GET — list templates
        orderTemplates.MapGet("/", async (bool? includeInactive, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderTemplatesQuery(includeInactive ?? false));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // GET /{id}
        orderTemplates.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetOrderTemplateByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // PATCH /{id}
        orderTemplates.MapMethods("/{id:guid}", ["PATCH"],
            async (Guid id, UpdateOrderTemplateRequest req, ISender sender) =>
            {
                IReadOnlyList<OrderTemplateMission> missions;
                try
                {
                    missions = OrderTemplateMissionParser.ParseAll(req.TransportOrder?.Missions);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ex.Message);
                }

                var transport = req.TransportOrder!;
                var result = await sender.Send(new UpdateOrderTemplateCommand(
                    Id: id,
                    Priority: req.Priority,
                    StructureType: string.IsNullOrWhiteSpace(transport.StructureType) ? "sequence" : transport.StructureType,
                    TransportOrderPriority: transport.Priority ?? req.Priority,
                    Missions: missions,
                    AppointVehicleKey: req.AppointVehicleKey,
                    AppointVehicleName: req.AppointVehicleName,
                    AppointVehicleGroupKey: req.AppointVehicleGroupKey,
                    AppointVehicleGroupName: req.AppointVehicleGroupName,
                    AppointQueueWaitArea: req.AppointQueueWaitArea,
                    Description: req.Description));
                return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
            });

        // POST /{id}/activate, /deactivate
        orderTemplates.MapPost("/{id:guid}/activate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SetOrderTemplateActiveCommand(id, true));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        orderTemplates.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new SetOrderTemplateActiveCommand(id, false));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // DELETE /{id}
        orderTemplates.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new DeleteOrderTemplateCommand(id));
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });

        // POST /{id}/instantiate — resolve ActionTemplate references against
        // the catalog and POST the full envelope to RIOT3. dryRun=true skips
        // the vendor call so operators can preview the resolved missions.
        orderTemplates.MapPost("/{id:guid}/instantiate",
            async (Guid id, InstantiateOrderTemplateRequest? req, ISender sender) =>
            {
                req ??= new InstantiateOrderTemplateRequest();
                var result = await sender.Send(new InstantiateOrderTemplateCommand(
                    OrderTemplateId: id,
                    PriorityOverride: req.Priority,
                    AppointVehicleKeyOverride: req.AppointVehicleKey,
                    AppointVehicleNameOverride: req.AppointVehicleName,
                    AppointVehicleGroupKeyOverride: req.AppointVehicleGroupKey,
                    AppointVehicleGroupNameOverride: req.AppointVehicleGroupName,
                    AppointQueueWaitAreaOverride: req.AppointQueueWaitArea,
                    UpperKey: req.UpperKey,
                    DryRun: req.DryRun ?? false));
                return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
            });
    }
}

// Body is optional — all fields are overrides on top of the stored template.
// DryRun=true makes the API return the resolved envelope without calling
// RIOT3; useful for previewing what would be sent.
public record InstantiateOrderTemplateRequest(
    int? Priority = null,
    string? AppointVehicleKey = null,
    string? AppointVehicleName = null,
    string? AppointVehicleGroupKey = null,
    string? AppointVehicleGroupName = null,
    string? AppointQueueWaitArea = null,
    string? UpperKey = null,
    bool? DryRun = null);

// ── OrderTemplate request DTOs (RIOT3 wire shape) ─────────────────────────────
public record CreateOrderTemplateRequest(
    string Name,
    int Priority,
    TransportOrderRequest TransportOrder,
    string? AppointVehicleKey = null,
    string? AppointVehicleName = null,
    string? AppointVehicleGroupKey = null,
    string? AppointVehicleGroupName = null,
    string? AppointQueueWaitArea = null,
    string? Description = null);

public record UpdateOrderTemplateRequest(
    int Priority,
    TransportOrderRequest TransportOrder,
    string? AppointVehicleKey = null,
    string? AppointVehicleName = null,
    string? AppointVehicleGroupKey = null,
    string? AppointVehicleGroupName = null,
    string? AppointQueueWaitArea = null,
    string? Description = null);

public record TransportOrderRequest(
    string? StructureType = null,
    int? Priority = null,
    List<MissionRequest>? Missions = null);

// A mission in the request — fields are nullable because MOVE vs ACT (and
// inline ACT vs reference ACT) use different subsets. Parser enforces the
// per-variant requirements.
public record MissionRequest(
    string Type,
    string? Category = null,
    int? MapId = null,
    int? StationId = null,
    string? ActionType = null,
    string? BlockingType = null,
    List<ActionParameterDto>? ActionParameters = null,
    string? ActionTemplateName = null);

internal static class OrderTemplateMissionParser
{
    public static IReadOnlyList<OrderTemplateMission> ParseAll(IReadOnlyList<MissionRequest>? missions)
    {
        if (missions is null || missions.Count == 0)
            throw new ArgumentException("transportOrder.missions is required and must contain at least one mission.");

        var result = new List<OrderTemplateMission>(missions.Count);
        for (var i = 0; i < missions.Count; i++)
        {
            try
            {
                result.Add(ParseOne(i + 1, missions[i]));
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Mission {i + 1}: {ex.Message}", ex);
            }
        }
        return result;
    }

    private static OrderTemplateMission ParseOne(int sequence, MissionRequest m)
    {
        var category = string.IsNullOrWhiteSpace(m.Category) ? "agv" : m.Category;
        var type = (m.Type ?? string.Empty).Trim().ToUpperInvariant();

        return type switch
        {
            "MOVE" => ParseMove(sequence, category, m),
            "ACT"  => ParseAct(sequence, category, m),
            _      => throw new ArgumentException($"Unknown mission type '{m.Type}'. Expected 'MOVE' or 'ACT'.")
        };
    }

    private static OrderTemplateMission ParseMove(int sequence, string category, MissionRequest m)
    {
        if (!m.MapId.HasValue || !m.StationId.HasValue)
            throw new ArgumentException("MOVE mission requires 'mapId' and 'stationId'.");
        return OrderTemplateMission.CreateMove(sequence, category, m.MapId.Value, m.StationId.Value);
    }

    private static OrderTemplateMission ParseAct(int sequence, string category, MissionRequest m)
    {
        var hasReference = !string.IsNullOrWhiteSpace(m.ActionTemplateName);
        var hasInline    = !string.IsNullOrWhiteSpace(m.ActionType) || (m.ActionParameters?.Count ?? 0) > 0;

        if (hasReference && hasInline)
            throw new ArgumentException(
                "ACT mission must use either 'actionTemplateName' OR inline params (actionType + actionParameters), not both.");
        if (!hasReference && !hasInline)
            throw new ArgumentException(
                "ACT mission must provide either 'actionTemplateName' or inline params (actionType + actionParameters).");

        if (hasReference)
        {
            return OrderTemplateMission.CreateActByReference(sequence, category, m.ActionTemplateName!, m.BlockingType);
        }

        if (string.IsNullOrWhiteSpace(m.ActionType))
            throw new ArgumentException("Inline ACT mission requires 'actionType'.");

        var parameters = (m.ActionParameters ?? new List<ActionParameterDto>())
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .Select(p => new MissionActionParameter(p.Key, JsonValueToString(p.Value)))
            .ToList();

        var blockingType = string.IsNullOrWhiteSpace(m.BlockingType) ? "NONE" : m.BlockingType;
        return OrderTemplateMission.CreateActInline(sequence, category, m.ActionType!, blockingType, parameters);
    }

    // Mission params land in storage as strings so the DB schema stays simple;
    // the response handler reparses to int where appropriate. Null/Undefined
    // values are kept as null.
    private static string? JsonValueToString(System.Text.Json.JsonElement? value)
    {
        if (!value.HasValue) return null;
        var v = value.Value;
        return v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined => null,
            System.Text.Json.JsonValueKind.String => v.GetString(),
            System.Text.Json.JsonValueKind.Number => v.GetRawText(),
            System.Text.Json.JsonValueKind.True   => "true",
            System.Text.Json.JsonValueKind.False  => "false",
            _                                     => v.GetRawText()
        };
    }
}

// Request body mirrors RIOT3's /api/v4/order/action-templates payload:
//   { actionName, actionType, actionParameters: [{key,value},...] }
// Property order matters: Swagger renders fields in declaration order, so we
// keep actionType right after actionName to match the RIOT3 spec example.
// ActionParameters is declared nullable (with default null) so it can sit
// after the optional ActionType — the parser rejects null/empty with a
// clearer 400 message than the framework's missing-property error.
public record CreateActionTemplateRequest(
    string ActionName,
    string? ActionType = null,
    List<ActionParameterDto>? ActionParameters = null,
    string? Description = null);

public record UpdateActionTemplateRequest(
    string? ActionType = null,
    List<ActionParameterDto>? ActionParameters = null,
    string? Description = null);

// One entry in the actionParameters array. RIOT3 sends `value` as JSON
// (int for id/param0/param1, string for param_str, or omitted entirely
// when the key is just being declared with no value).
public record ActionParameterDto
{
    public string Key { get; init; } = string.Empty;
    public System.Text.Json.JsonElement? Value { get; init; }
}

// Result of pulling the four well-known parameters out of the array.
internal sealed record ActionTemplateParameterSet(int Id, int Param0, int Param1, string? ParamStr);

internal static class ActionParameterParser
{
    private const string KeyId       = "id";
    private const string KeyParam0   = "param0";
    private const string KeyParam1   = "param1";
    private const string KeyParamStr = "param_str";

    public static ActionTemplateParameterSet Parse(IReadOnlyList<ActionParameterDto>? entries)
    {
        if (entries is null || entries.Count == 0)
            throw new ArgumentException(
                $"actionParameters is required (must include '{KeyId}', '{KeyParam0}', '{KeyParam1}').",
                nameof(entries));

        var map = new Dictionary<string, ActionParameterDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Key)) continue;
            map[e.Key.Trim()] = e;
        }

        var id       = RequireInt(map, KeyId);
        var param0   = RequireInt(map, KeyParam0);
        var param1   = RequireInt(map, KeyParam1);
        var paramStr = OptionalString(map, KeyParamStr);

        return new ActionTemplateParameterSet(id, param0, param1, paramStr);
    }

    private static int RequireInt(IReadOnlyDictionary<string, ActionParameterDto> map, string key)
    {
        if (!map.TryGetValue(key, out var entry) || !entry.Value.HasValue
            || entry.Value.Value.ValueKind == System.Text.Json.JsonValueKind.Null
            || entry.Value.Value.ValueKind == System.Text.Json.JsonValueKind.Undefined)
        {
            throw new ArgumentException($"actionParameters: '{key}' is required.");
        }

        var v = entry.Value.Value;
        if (v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var i))
            return i;

        // Allow numeric strings (e.g. "131") so operators can paste UI-edited values.
        if (v.ValueKind == System.Text.Json.JsonValueKind.String
            && int.TryParse(v.GetString(), out var parsed))
            return parsed;

        throw new ArgumentException($"actionParameters: '{key}' must be an integer.");
    }

    private static string? OptionalString(IReadOnlyDictionary<string, ActionParameterDto> map, string key)
    {
        if (!map.TryGetValue(key, out var entry) || !entry.Value.HasValue
            || entry.Value.Value.ValueKind == System.Text.Json.JsonValueKind.Null
            || entry.Value.Value.ValueKind == System.Text.Json.JsonValueKind.Undefined)
        {
            // Key declared with no value (RIOT3-style `{ "key": "param_str" }`)
            // is treated the same as "not provided".
            return null;
        }

        var v = entry.Value.Value;
        return v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : v.ToString();
    }
}
