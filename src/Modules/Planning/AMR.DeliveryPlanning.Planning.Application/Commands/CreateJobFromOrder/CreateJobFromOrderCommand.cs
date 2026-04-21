using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateJobFromOrder;

public record CreateJobFromOrderCommand(
    Guid DeliveryOrderId,
    Guid PickupStationId,
    Guid DropStationId,
    string Priority
) : ICommand<Guid>;
