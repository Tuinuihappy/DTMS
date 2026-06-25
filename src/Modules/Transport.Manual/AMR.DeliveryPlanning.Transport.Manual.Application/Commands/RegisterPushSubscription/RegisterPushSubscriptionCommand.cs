using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Manual.Application.Commands.RegisterPushSubscription;

// POST /api/operator/devices/register-push — PWA's Service Worker
// subscribes to Web Push and POSTs the resulting PushSubscription JSON
// to register the endpoint + keys with DTMS (per ADR-013). Re-issuing
// the same endpoint updates keys in place (browsers rotate keys).
public record RegisterPushSubscriptionCommand(
    Guid OperatorId,
    PushPlatform Platform,
    string Endpoint,
    string? PublicKey,
    string? AuthSecret,
    string? DeviceLabel) : ICommand;
