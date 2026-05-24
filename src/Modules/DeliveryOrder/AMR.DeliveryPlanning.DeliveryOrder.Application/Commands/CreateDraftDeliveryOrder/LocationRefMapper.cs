using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

internal static class LocationRefMapper
{
    public static LocationRef ToDomain(this LocationRefDto dto) =>
        dto.Code is not null
            ? LocationRef.FromCode(dto.Code)
            : LocationRef.FromStationId(dto.StationId!.Value);
}
