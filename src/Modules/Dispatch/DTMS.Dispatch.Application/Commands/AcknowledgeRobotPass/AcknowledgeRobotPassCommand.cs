using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.AcknowledgeRobotPass;

public record AcknowledgeRobotPassCommand(Guid TripId) : ICommand;
