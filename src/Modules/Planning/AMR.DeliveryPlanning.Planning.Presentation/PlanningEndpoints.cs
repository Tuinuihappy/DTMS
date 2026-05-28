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

        // POST — create a new template
        actionTemplates.MapPost("/", async (CreateActionTemplateRequest req, ISender sender) =>
        {
            var result = await sender.Send(new CreateActionTemplateCommand(
                req.Name, req.ActionType, req.VendorActionId, req.Param0, req.Param1, req.ParamStr, req.Description));
            return result.IsSuccess
                ? Results.Created($"/api/v1/planning/action-templates/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // GET — list templates (filter by ActionType, optionally include inactive)
        actionTemplates.MapGet("/", async (bool includeInactive, string? actionType, ISender sender) =>
        {
            var result = await sender.Send(new GetActionTemplatesQuery(includeInactive, actionType));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // GET /{id} — fetch one
        actionTemplates.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetActionTemplateByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // PATCH /{id} — update the action params + meta (rename is separate)
        actionTemplates.MapMethods("/{id:guid}", ["PATCH"],
            async (Guid id, UpdateActionTemplateRequest req, ISender sender) =>
            {
                var result = await sender.Send(new UpdateActionTemplateCommand(
                    id, req.ActionType, req.VendorActionId, req.Param0, req.Param1, req.ParamStr, req.Description));
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

public record CreateActionTemplateRequest(
    string Name,
    string ActionType,
    int VendorActionId,
    int Param0,
    int Param1,
    string? ParamStr = null,
    string? Description = null);

public record UpdateActionTemplateRequest(
    string ActionType,
    int VendorActionId,
    int Param0,
    int Param1,
    string? ParamStr = null,
    string? Description = null);
