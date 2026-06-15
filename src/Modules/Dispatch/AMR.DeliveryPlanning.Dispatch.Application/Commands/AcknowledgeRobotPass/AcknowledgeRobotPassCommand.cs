using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.AcknowledgeRobotPass;

public record AcknowledgeRobotPassCommand(Guid TripId) : ICommand;
