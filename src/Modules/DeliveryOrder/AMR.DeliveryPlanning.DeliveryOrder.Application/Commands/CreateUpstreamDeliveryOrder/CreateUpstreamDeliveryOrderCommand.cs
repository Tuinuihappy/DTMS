using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

public record CreateUpstreamDeliveryOrderCommand(
    string OrderRef,
    ServiceWindowDto ServiceWindow,
    List<ItemDto> Items,
    SourceSystem SourceSystem,
    Priority Priority = Priority.Normal,
    string? RequestedBy = null,
    string? Notes = null,
    TransportMode? RequestedTransportMode = TransportMode.Amr,
    bool? RequiresPod = null
) : ICommand<UpstreamOrderAckDto>;

public record UpstreamOrderAckDto(
    Guid Id,
    string OrderRef,
    OrderStatus Status,
    DateTime AcceptedAt,
    IReadOnlyList<OrderQualityIssue> Warnings);
