using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.DispatchTrip;

public record DispatchLegInfo(Guid FromStationId, Guid ToStationId, int SequenceOrder);

public record DispatchTripCommand(Guid JobId, Guid VehicleId, List<DispatchLegInfo> Legs) : ICommand<Guid>;
