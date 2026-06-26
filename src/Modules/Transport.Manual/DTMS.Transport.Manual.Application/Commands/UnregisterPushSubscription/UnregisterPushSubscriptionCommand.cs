using DTMS.SharedKernel.Messaging;

namespace DTMS.Transport.Manual.Application.Commands.UnregisterPushSubscription;

// DELETE /api/operator/devices/push — operator unsubscribes from the
// device (logout, settings toggle). Idempotent — no-op if endpoint
// already absent.
public record UnregisterPushSubscriptionCommand(Guid OperatorId, string Endpoint) : ICommand;
