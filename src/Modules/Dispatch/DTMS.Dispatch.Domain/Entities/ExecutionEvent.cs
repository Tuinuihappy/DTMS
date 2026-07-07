using DTMS.SharedKernel.Domain;

namespace DTMS.Dispatch.Domain.Entities;

public class ExecutionEvent : Entity<Guid>
{
    public Guid TripId { get; private set; }
    public Guid? TaskId { get; private set; }
    public string EventType { get; private set; }
    public string? Details { get; private set; }

    // WHO drove the action and WHEN they did it upstream. Populated by the
    // federated /api/v1/source/trips/* actions, where a machine caller forwards
    // the human actor from its own system. Both NULL for events with no external
    // actor (AMR webhooks, projections, internal transitions). ActedAt is the
    // source-reported action time; OccurredAt below stays the DTMS receive time,
    // so a lagged/batched upstream call keeps both truths.
    public string? Actor { get; private set; }
    public DateTime? ActedAt { get; private set; }

    public DateTime OccurredAt { get; private set; }

    private ExecutionEvent() { EventType = null!; }

    internal ExecutionEvent(Guid tripId, Guid? taskId, string eventType, string? details,
        string? actor = null, DateTime? actedAt = null)
    {
        Id = Guid.NewGuid();
        TripId = tripId;
        TaskId = taskId;
        EventType = eventType;
        Details = details;
        Actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
        ActedAt = NormalizeToUtc(actedAt);
        OccurredAt = DateTime.UtcNow;
    }

    // Source systems send ActedAt as ISO-8601 that may carry an offset
    // (e.g. +07:00) or no zone at all. System.Text.Json binds those to a
    // DateTime with Kind=Local/Unspecified, which Npgsql refuses to write to a
    // `timestamp with time zone` column ("only UTC is supported") — surfacing
    // as a 500 on save. Coerce to UTC at the single persistence chokepoint:
    // offset-bearing values are already adjusted to the machine zone by the
    // JSON binder, so ToUniversalTime() recovers the correct instant; a bare
    // (Unspecified) value is assumed to already be UTC.
    internal static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (value is not { } v) return null;
        return v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        };
    }
}
