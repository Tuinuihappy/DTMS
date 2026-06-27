using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;
using DetailDto = DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder.DeliveryOrderDetailDto;

namespace DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

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
