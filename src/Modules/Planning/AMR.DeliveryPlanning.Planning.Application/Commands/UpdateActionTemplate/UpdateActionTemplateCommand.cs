using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.UpdateActionTemplate;

// Full replacement: vendor-action quartet + meta + name. Renaming runs a
// catalog-wide uniqueness check (case-insensitive). OrderTemplate refs that
// pointed at the old name are not auto-rewritten — operators rename and
// update the affected order-templates in the same change window.
public record UpdateActionTemplateCommand(
    Guid Id,
    string ActionName,
    ActionType ActionType,
    int VendorActionId,
    int Param0,
    int Param1,
    string? ParamStr = null
) : ICommand;
