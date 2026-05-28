using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;

// Mirrors the RIOT3 "New action template" form:
//   Template name + Action template (= ActionType) + ID + param0 + param1 + param_str
// ActionType defaults to "STD" matching the RIOT3 API request schema default.
public record CreateActionTemplateCommand(
    string Name,
    int VendorActionId,
    int Param0,
    int Param1,
    string ActionType = "STD",
    string? ParamStr = null,
    string? Description = null
) : ICommand<Guid>;
