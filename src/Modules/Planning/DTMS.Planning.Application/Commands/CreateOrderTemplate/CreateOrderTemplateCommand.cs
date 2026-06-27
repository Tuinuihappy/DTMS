using AMR.DeliveryPlanning.Planning.Application.Queries.GetOrderTemplates;
using AMR.DeliveryPlanning.Planning.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateOrderTemplate;

// Internal application-layer command shape. The Presentation layer parses
// the RIOT3-shaped wire format into this richer (already-validated) model
// before sending it through the handler.
//
// Returns the projected OrderTemplateDto so the POST response can echo
// the full created resource — matches the RIOT3 `data` envelope shape and
// saves the client a follow-up GET to learn the assigned id + audit fields.
public record CreateOrderTemplateCommand(
    string Name,
    int Priority,
    string StructureType,
    int TransportOrderPriority,
    IReadOnlyList<OrderTemplateMission> Missions,
    string? AppointVehicleKey = null,
    string? AppointVehicleName = null,
    string? AppointVehicleGroupKey = null,
    string? AppointVehicleGroupName = null,
    string? AppointQueueWaitArea = null,
    string? Description = null,
    Guid? PickupStationId = null,
    Guid? DropStationId = null
) : ICommand<OrderTemplateDto>;
