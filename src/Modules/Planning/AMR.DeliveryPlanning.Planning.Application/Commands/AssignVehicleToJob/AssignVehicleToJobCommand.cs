using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.AssignVehicleToJob;

public record AssignVehicleToJobCommand(Guid JobId) : ICommand;
