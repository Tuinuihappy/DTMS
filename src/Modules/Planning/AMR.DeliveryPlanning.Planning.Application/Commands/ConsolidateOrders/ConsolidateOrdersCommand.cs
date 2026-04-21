using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.ConsolidateOrders;

public record ConsolidateOrdersCommand(
    List<Guid> OrderIds,
    Guid PickupStationId,
    Guid DropStationId,
    string Priority = "Normal",
    string? RequiredCapability = null
) : ICommand<Guid>;
