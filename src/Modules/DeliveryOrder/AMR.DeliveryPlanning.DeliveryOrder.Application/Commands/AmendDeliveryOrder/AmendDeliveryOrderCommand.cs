using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.AmendDeliveryOrder;

public record AmendServiceWindowDto(DateTime? Earliest, DateTime? Latest);

public record AmendDeliveryOrderCommand(
    Guid OrderId,
    string Reason,
    AmendServiceWindowDto? NewServiceWindow,
    string? AmendedBy) : ICommand<Guid>;
