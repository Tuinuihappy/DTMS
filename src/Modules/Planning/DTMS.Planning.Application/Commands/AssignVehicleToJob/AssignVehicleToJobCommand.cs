using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.AssignVehicleToJob;

public record AssignVehicleToJobCommand(Guid JobId) : ICommand;
