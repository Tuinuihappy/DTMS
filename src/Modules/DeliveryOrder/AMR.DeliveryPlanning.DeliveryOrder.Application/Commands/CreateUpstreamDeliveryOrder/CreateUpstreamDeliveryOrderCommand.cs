using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using DetailDto = AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder.DeliveryOrderDetailDto;

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
    bool? RequiresDropPod = null,
    bool? RequiresPickupPod = null
) : ICommand<UpstreamOrderAckDto>;

public record UpstreamOrderAckDto(
    DetailDto Order,
    IReadOnlyList<OrderQualityIssue> Warnings);
