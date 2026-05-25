using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

public record CreateUpstreamDeliveryOrderCommand(
    string OrderRef,
    DateTime RequestedDeliveryDate,
    List<ItemDto> Items,
    Priority Priority,
    SourceSystem SourceSystem,
    string CreatedBy,
    SlaTier SlaTier = SlaTier.Bronze
) : ICommand<UpstreamOrderAckDto>;

public record UpstreamOrderAckDto(
    Guid Id,
    string OrderRef,
    OrderStatus Status,
    DateTime AcceptedAt,
    IReadOnlyList<OrderQualityIssue> Warnings);
