using MassTransit;

namespace DTMS.Planning.Infrastructure.Sagas;

/// <summary>
/// T2 — persisted row for the order-to-trip orchestration. Implements
/// <see cref="SagaStateMachineInstance"/> (MassTransit contract — every saga
/// instance has a CorrelationId) and <see cref="ISagaVersion"/> (opt-in
/// optimistic concurrency via the Version column).
///
/// <para>CorrelationId = the DeliveryOrder's id, so an event for a given
/// order always routes to the same saga row regardless of which pod
/// consumes it. CurrentState is the integer projection of
/// <see cref="OrderSagaState"/> — the EF mapping configures the conversion.</para>
///
/// <para>POC scope: nullable JobId / TripId / VendorMissionId are wired but
/// only JobId is populated by the implemented transitions. The other two
/// land in Phase 2 (TripDispatched, Riot3MissionAccepted handlers).</para>
/// </summary>
public class DeliveryOrderSagaInstance : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    /// <summary>Integer projection of <see cref="OrderSagaState"/>.</summary>
    public int CurrentState { get; set; }

    public Guid? JobId { get; set; }
    public Guid? TripId { get; set; }
    public string? VendorMissionId { get; set; }

    /// <summary>
    /// Last fault payload kept verbatim so ops can grep without going to
    /// log storage. Bounded length so a long stack trace doesn't blow up
    /// the row size.
    /// </summary>
    public string? LastFaultMessage { get; set; }

    /// <summary>Bumped every time the saga retries after a fault.</summary>
    public int RetryCount { get; set; }

    /// <summary>Optimistic concurrency token managed by EF.</summary>
    public int Version { get; set; }

    /// <summary>Bumped on every transition for observability + replay.</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
