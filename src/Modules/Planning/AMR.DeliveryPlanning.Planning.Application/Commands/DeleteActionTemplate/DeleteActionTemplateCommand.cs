using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.DeleteActionTemplate;

// Hard-delete the template. Preferred for catalog cleanup. Future-Phase-1C
// will add a "referenced-by OrderTemplate" check before allowing delete.
// For now we only check IsActive=false as a soft signal that the template
// is no longer in use.
public record DeleteActionTemplateCommand(Guid Id) : ICommand;
