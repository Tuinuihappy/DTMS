using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;

public record CreateJobFromOrderCommand(
    Guid DeliveryOrderId,
    Guid PickupStationId,
    Guid DropStationId,
    string Priority,
    List<Guid>? AdditionalDropStationIds = null,
    string? RequiredCapability = null,
    double TotalWeight = 0,
    string? RequestedTransportMode = null,
    DateTime? SlaDeadline = null
) : ICommand<Guid>;
