using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.InstantiateOrderTemplate;

// Take a stored OrderTemplate, expand every ActionTemplateName reference
// against the catalog, and POST the result to RIOT3 as a new order.
// Optional fields override the template's defaults at instantiation time
// so the same template can fan out to different vehicles / priorities.
public record InstantiateOrderTemplateCommand(
    Guid OrderTemplateId,
    int? PriorityOverride = null,
    string? AppointVehicleKeyOverride = null,
    string? AppointVehicleNameOverride = null,
    string? AppointVehicleGroupKeyOverride = null,
    string? AppointVehicleGroupNameOverride = null,
    string? AppointQueueWaitAreaOverride = null,
    string? UpperKey = null,           // caller-supplied correlation id; defaults to a new Guid
    bool DryRun = false                 // skip the actual RIOT3 call and just return the resolved envelope
) : ICommand<InstantiateOrderTemplateResult>;

// Result carries the resolved order (what we would send / did send) plus the
// upperKey we used and the RIOT3 orderKey we got back (null when dryRun=true
// or RIOT3 didn't return one).
public record InstantiateOrderTemplateResult(
    string UpperKey,
    string? Riot3OrderKey,
    ResolvedOrder ResolvedOrder,
    bool DryRun);
