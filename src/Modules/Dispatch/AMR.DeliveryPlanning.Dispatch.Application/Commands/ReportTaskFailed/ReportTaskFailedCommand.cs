using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ReportTaskFailed;

public record ReportTaskFailedCommand(Guid TripId, Guid TaskId, string Reason) : ICommand;
