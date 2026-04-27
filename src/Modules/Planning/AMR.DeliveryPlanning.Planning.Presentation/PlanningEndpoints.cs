using AMR.DeliveryPlanning.Planning.Application.Commands.AssignVehicleToJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.CommitPlan;
using AMR.DeliveryPlanning.Planning.Application.Commands.ConsolidateOrders;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateCrossDockJobs;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateMilkRun;
using AMR.DeliveryPlanning.Planning.Application.Commands.CreateMultiPickDropJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.ReplanJob;
using AMR.DeliveryPlanning.Planning.Application.Commands.UpdateCostModel;
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
        var group = app.MapGroup("/api/planning/jobs").WithTags("Planning").RequireAuthorization();

        // POST /api/planning/jobs — Create a Job from a DeliveryOrder
        group.MapPost("/", async (CreateJobFromOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/planning/jobs/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/planning/jobs/{id}/assign — Assign a vehicle (Greedy)
        group.MapPost("/{id:guid}/assign", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new AssignVehicleToJobCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/planning/jobs/{id}/commit — Commit the plan
        group.MapPost("/{id:guid}/commit", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new CommitPlanCommand(id));
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // POST /api/planning/jobs/{id}/replan — Replan a committed job
        group.MapPost("/{id:guid}/replan", async (Guid id, ReplanJobCommand command, ISender sender) =>
        {
            if (id != command.JobId) return Results.BadRequest("ID mismatch");
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
        });

        // GET /api/planning/jobs/{id} — Get job details
        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var result = await sender.Send(new GetJobByIdQuery(id));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });

        // GET /api/planning/jobs/pending — Get all pending jobs
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
                ? Results.Created($"/api/planning/jobs/{result.Value}", result.Value)
                : Results.BadRequest(result.Error);
        });

        // POST /api/planning/cross-dock — Create linked inbound/outbound cross-dock jobs
        planningGroup.MapPost("/cross-dock", async (CreateCrossDockJobsCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess
                ? Results.Created($"/api/planning/jobs/{result.Value.InboundJobId}", result.Value)
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
                ? Results.Created($"/api/planning/jobs/{result.Value}", result.Value)
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
    }
}
