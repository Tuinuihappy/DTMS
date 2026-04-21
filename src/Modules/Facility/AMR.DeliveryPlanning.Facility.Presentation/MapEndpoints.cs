using AMR.DeliveryPlanning.Facility.Application.Commands.CreateMap;
using AMR.DeliveryPlanning.Facility.Application.Queries.GetMapById;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.Facility.Presentation;

public static class MapEndpoints
{
    public static void MapFacilityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/facility/maps").WithTags("Facility").RequireAuthorization();

        group.MapPost("/", async (CreateMapCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
        {
            var query = new GetMapByIdQuery(id);
            var result = await sender.Send(query);
            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });
    }
}
