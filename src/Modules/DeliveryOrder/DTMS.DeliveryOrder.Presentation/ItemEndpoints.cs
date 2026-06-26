using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetItem;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.SearchItems;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation;

public static class ItemEndpoints
{
    public static void MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items").WithTags("Items").RequireAuthorization();

        group.MapGet("/", async (
            string? itemId,
            string? status,
            string? pickupCode,
            Guid? pickupStationId,
            string? dropCode,
            Guid? dropStationId,
            ISender sender,
            int page = 1,
            int pageSize = 20) =>
        {
            ItemStatus? statusEnum = status != null && Enum.TryParse<ItemStatus>(status, true, out var s) ? s : null;

            var result = await sender.Send(new SearchItemsQuery(
                itemId, statusEnum,
                pickupCode, pickupStationId,
                dropCode, dropStationId,
                page, pageSize));

            return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
        });

        // GET /api/v1/items/{itemId}
        group.MapGet("/{itemId:guid}", async (Guid itemId, ISender sender) =>
        {
            var result = await sender.Send(new GetItemQuery(itemId));
            return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound(result.Error);
        });
    }
}
