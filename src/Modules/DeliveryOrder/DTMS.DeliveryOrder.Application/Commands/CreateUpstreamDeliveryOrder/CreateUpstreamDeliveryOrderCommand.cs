using DTMS.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using DTMS.DeliveryOrder.Application.QualityIssues;
using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.SharedKernel.Messaging;
using Priority = DTMS.DeliveryOrder.Domain.Enums.Priority;
using TransportMode = DTMS.DeliveryOrder.Domain.Enums.TransportMode;
using DetailDto = DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder.DeliveryOrderDetailDto;

namespace DTMS.DeliveryOrder.Application.Commands.CreateUpstreamDeliveryOrder;

// Phase P4: SourceSystem enum replaced with SourceSystemKey string.
// Phase S.8e (P3): the endpoint at /api/v1/source/delivery-orders sets
// SourceSystemKey from the SystemPrincipal that
// SystemClientAuthMiddleware stamps out of the JWT sub claim — no wire
// field carries it. RequestedBy stays on the wire because external
// callers legitimately pass the human on their side (unlike the UI path).
public record CreateUpstreamDeliveryOrderCommand(
    string OrderRef,
    ServiceWindowDto ServiceWindow,
    List<ItemDto> Items,
    string SourceSystemKey,
    Priority Priority = Priority.Normal,
    string? RequestedBy = null,
    string? Notes = null,
    TransportMode? RequestedTransportMode = TransportMode.Amr,
    bool? RequiresDropPod = null,
    bool? RequiresPickupPod = null,
    // When true, the source system executes transport itself: DTMS auto-acks
    // + auto-picks-up the trip (attributed to RequestedBy) and the source
    // reports drop + complete. RequestedBy becomes required.
    bool SelfManaged = false
) : ICommand<UpstreamOrderAckDto>;

public record UpstreamOrderAckDto(
    DetailDto Order,
    IReadOnlyList<OrderQualityIssue> Warnings);
