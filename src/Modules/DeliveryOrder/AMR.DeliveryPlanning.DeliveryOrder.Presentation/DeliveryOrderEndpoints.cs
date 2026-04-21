using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CancelDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation;

public static class DeliveryOrderEndpoints
{
    public static void MapDeliveryOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/delivery-orders").WithTags("DeliveryOrders");

        group.MapPost("/", async (SubmitDeliveryOrderCommand command, ISender sender) =>
        {
            var result = await sender.Send(command);
            if (result.IsSuccess)
            {
                return Results.Created($"/api/delivery-orders/{result.Value}", result.Value);
            }
            return Results.BadRequest(result.Error);
        });

        group.MapDelete("/{id:guid}", async (Guid id, string reason, ISender sender) =>
        {
            var command = new CancelDeliveryOrderCommand(id, reason);
            var result = await sender.Send(command);
            return result.IsSuccess ? Results.NoContent() : Results.BadRequest(result.Error);
        });
    }
}
