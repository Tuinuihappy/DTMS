using AMR.DeliveryPlanning.Planning.Domain.Entities;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateOrderTemplate;

// Update everything except Name (rename is a separate concern due to its
// uniqueness check and the ripple risk into OrderGroup references later).
public record UpdateOrderTemplateCommand(
    Guid Id,
    int Priority,
    string StructureType,
    int TransportOrderPriority,
    IReadOnlyList<OrderTemplateMission> Missions,
    string? AppointVehicleKey = null,
    string? AppointVehicleName = null,
    string? AppointVehicleGroupKey = null,
    string? AppointVehicleGroupName = null,
    string? AppointQueueWaitArea = null,
    string? Description = null
) : ICommand;
