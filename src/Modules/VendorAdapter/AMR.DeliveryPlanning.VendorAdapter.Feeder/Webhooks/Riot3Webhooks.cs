using AMR.DeliveryPlanning.Fleet.IntegrationEvents;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Feeder.Webhooks;

public static class Riot3Webhooks
{
    public static void MapRiot3Webhooks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/webhooks/riot3").WithTags("Webhooks");

        group.MapPost("/status", async (RiotStatusPayload payload, IEventBus eventBus, ILogger<RiotStatusPayload> logger) =>
        {
            logger.LogInformation("Received status update from Riot3 robot {RobotId}: State={State}, Battery={Battery}", payload.RobotId, payload.State, payload.Battery);

            // In a real system, you'd map payload.RobotId (which might be a string) to the internal VehicleId Guid.
            // Assuming RobotId here is parsable to Guid for simplicity:
            if (!Guid.TryParse(payload.RobotId, out var vehicleId))
            {
                return Results.BadRequest("Invalid RobotId format. Must be Guid.");
            }

            var integrationEvent = new VehicleStateChangedIntegrationEvent(
                EventId: Guid.NewGuid(),
                OccurredOn: DateTime.UtcNow,
                VehicleId: vehicleId,
                State: MapRiotStateToCanonical(payload.State),
                BatteryLevel: payload.Battery,
                CurrentNodeId: payload.CurrentNode != null ? Guid.Parse(payload.CurrentNode) : null
            );

            await eventBus.PublishAsync(integrationEvent, CancellationToken.None);

            return Results.Ok();
        });
    }

    private static string MapRiotStateToCanonical(string riotState)
    {
        // Simplistic mapping
        return riotState.ToLower() switch
        {
            "idle" => "Idle",
            "running" => "Moving",
            "error" => "Error",
            "charging" => "Charging",
            "working" => "Working",
            _ => "Offline"
        };
    }
}
