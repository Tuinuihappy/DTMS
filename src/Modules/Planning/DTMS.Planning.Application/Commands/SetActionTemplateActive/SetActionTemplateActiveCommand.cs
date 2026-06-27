using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.SetActionTemplateActive;

// Soft-disable / re-enable a template. Preferred over Delete when the
// template might still be referenced by historical OrderTemplate runs.
public record SetActionTemplateActiveCommand(Guid Id, bool IsActive) : ICommand;
