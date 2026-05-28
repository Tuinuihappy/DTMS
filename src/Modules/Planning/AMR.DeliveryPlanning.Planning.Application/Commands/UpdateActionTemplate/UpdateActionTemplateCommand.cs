using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateActionTemplate;

// Updates the vendor-action quartet + meta. Name is changed via a separate
// command (RenameActionTemplate) because rename has stricter validation
// (uniqueness across the catalog) and ripple effects on OrderTemplate refs
// later in Phase 1C.
public record UpdateActionTemplateCommand(
    Guid Id,
    string ActionType,
    int VendorActionId,
    int Param0,
    int Param1,
    string? ParamStr = null,
    string? Description = null
) : ICommand;
