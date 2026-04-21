using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskCompleted;

public record ReportTaskCompletedCommand(Guid TripId, Guid TaskId) : ICommand;
