using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateMultiPickDropJob;

public record PickupDeliveryPair(Guid PickupStationId, Guid DropStationId, double Weight);

/// <summary>
/// Creates a Multi-Pick Multi-Drop (CVRPPD) job with precedence constraints.
/// Each pair: pick(i) must occur before drop(i).
/// </summary>
public record CreateMultiPickDropJobCommand(
    Guid DeliveryOrderId,
    List<PickupDeliveryPair> Pairs,
    string Priority = "Normal",
    string? RequiredCapability = null
) : ICommand<Guid>;
