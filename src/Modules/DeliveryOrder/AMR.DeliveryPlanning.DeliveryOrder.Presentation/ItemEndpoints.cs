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
            string? sku,
            string? cargoType,
            string? status,
            string? pickupCode,
            Guid? pickupStationId,
            string? dropCode,
            Guid? dropStationId,
            string? partNo,
            string? wo,
            string? line,
            string? vendor,
            string? dateCode,
            string? tradingCode,
            string? inventoryNo,
            string? po,
            string? traceId,
            string? lotNo,
            ISender sender,
            int page = 1,
            int pageSize = 20) =>
        {
            CargoType? cargoTypeEnum = cargoType != null && Enum.TryParse<CargoType>(cargoType, true, out var ct) ? ct : null;
            ItemStatus? statusEnum = status != null && Enum.TryParse<ItemStatus>(status, true, out var s) ? s : null;

            var result = await sender.Send(new SearchItemsQuery(
                sku, cargoTypeEnum, statusEnum,
                pickupCode, pickupStationId,
                dropCode, dropStationId,
                partNo, wo, line, vendor, dateCode, tradingCode,
                inventoryNo, po, traceId, lotNo,
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
