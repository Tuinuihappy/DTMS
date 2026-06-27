using DTMS.Planning.Application.Queries.GetActionTemplates;
using DTMS.Planning.Domain.Enums;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.CreateActionTemplate;

// Mirrors the RIOT3 "New action template" form:
//   Template name + Action template (= ActionCategory) + ID + param0 + param1 + param_str
// ActionCategory defaults to STD matching the RIOT3 API request schema default.
//
// ActionType is the literal RIOT3 wire string sent on every ACT mission
// resolved from this template (e.g. "standardRobotsCustom").
//
// Returns the projected ActionTemplateDto so the POST response can echo
// the full created resource — matches RIOT3's `data` envelope shape and
// saves the client a follow-up GET to learn the assigned id + audit fields.
public record CreateActionTemplateCommand(
    string Name,
    int VendorActionId,
    int Param0,
    int Param1,
    ActionCategory ActionCategory = ActionCategory.Std,
    string? ParamStr = null,
    string? ActionType = null
) : ICommand<ActionTemplateDto>;
