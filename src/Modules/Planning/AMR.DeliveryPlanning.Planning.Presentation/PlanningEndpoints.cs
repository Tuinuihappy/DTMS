using AMR.DeliveryPlanning.Planning.Application.Commands.AssignVehicleToJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.ConsolidateOrders;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateCrossDockJobs;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateMilkRun;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateMultiPickDropJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.DeleteActionTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.ReplanJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.SetActionTemplateActive;
using AMR.DeliveryPlanning.Planning.Application.Commands.UpdateActionTemplate;
using AMR.DeliveryPlanning.Planning.Application.Commands.UpdateCostModel;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplateById;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetCostModel;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetJobById;
using AMR.DeliveryPlanning.Planning.Application.Queries.GetPendingJobs;
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
    }
}

// Request body mirrors RIOT3's /api/v4/order/action-templates payload:
//   { actionName, actionType, actionParameters: [{key,value},...] }
// The parameters array is parsed into id/param0/param1/param_str by
// ActionParameterParser.
public record CreateActionTemplateRequest(
    string ActionName,
    List<ActionParameterDto> ActionParameters,
    string? ActionType = null,
    string? Description = null);

public record UpdateActionTemplateRequest(
    List<ActionParameterDto> ActionParameters,
    string? ActionType = null,
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
