using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.RetryJob;

/// <summary>
/// Phase b8 — Operator-initiated retry of a Failed Job. Resets the Job to
/// Created, increments AttemptNumber, and re-dispatches via the envelope
/// path. Result carries the new TripId on success; on dispatch failure the
/// Job is left in Failed status with the new reason and the caller sees
/// the failure surfaced.
/// </summary>
public record RetryJobCommand(Guid JobId) : ICommand<RetryJobResult>;

public sealed record RetryJobResult(
    Guid JobId,
    int NewAttemptNumber,
    bool Dispatched,
    Guid? NewTripId,
    string? VendorOrderKey,
    string? FailureReason);
