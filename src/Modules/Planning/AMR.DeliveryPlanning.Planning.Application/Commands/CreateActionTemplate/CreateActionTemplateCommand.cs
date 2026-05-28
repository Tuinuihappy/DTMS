using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateActionTemplate;

// Mirrors the RIOT3 "New action template" form:
//   Template name + Action template (= ActionType) + ID + param0 + param1 + param_str
public record CreateActionTemplateCommand(
    string Name,
    string ActionType,
    int VendorActionId,
    int Param0,
    int Param1,
    string? ParamStr = null,
    string? Description = null
) : ICommand<Guid>;
