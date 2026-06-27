namespace DTMS.Api.RobotPositions;

public static class RobotPositionEndpoints
{
    public static void MapRobotPositionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/facility")
            .WithTags("Facility")
            .RequireAuthorization();

        // GET /api/v1/facility/maps/{id}/robot-positions
        // Returns the latest snapshot from the in-memory store. The poller
        // refreshes the snapshot once per second; the frontend re-polls this
        // endpoint at the same cadence and interpolates between ticks.
        group.MapGet("/maps/{id:guid}/robot-positions", (Guid id, IRobotPositionStore store) =>
        {
            var positions = store.GetByMap(id);
            return Results.Ok(positions);
        });
    }
}
