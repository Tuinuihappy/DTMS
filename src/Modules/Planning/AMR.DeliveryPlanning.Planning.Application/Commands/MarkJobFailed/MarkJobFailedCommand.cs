using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.MarkJobFailed;

/// <summary>
/// Phase b8 — Called from DeliveryOrderValidatedConsumer when envelope
/// dispatch fails for one of 5 reasons: OrderTemplate not found, template
/// resolve fail, vendor 4xx/5xx, vendor 429, or vendor accepted but Trip
/// persistence failed (orphan).
/// </summary>
public record MarkJobFailedCommand(
    Guid JobId,
    string Reason
) : ICommand;
