using AMR.DeliveryPlanning.Planning.Application.Queries.GetActionTemplates;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;

// Mirrors the RIOT3 "New action template" form:
//   Template name + Action template (= ActionType) + ID + param0 + param1 + param_str
// ActionType defaults to STD matching the RIOT3 API request schema default.
//
// Returns the projected ActionTemplateDto so the POST response can echo
// the full created resource — matches RIOT3's `data` envelope shape and
// saves the client a follow-up GET to learn the assigned id + audit fields.
public record CreateActionTemplateCommand(
    string Name,
    int VendorActionId,
    int Param0,
    int Param1,
    ActionType ActionType = ActionType.Std,
    string? ParamStr = null
) : ICommand<ActionTemplateDto>;
