namespace DTMS.SharedKernel.Projection;

/// <summary>
/// Per-projector bookkeeping row that records which integration events have
/// already been processed by a given projector. Inserted in the SAME
/// transaction as the read-model write, so a successful save guarantees
/// the inbox row is durable. Redelivery of the same event finds the row
/// and short-circuits — at-least-once delivery becomes effectively-once.
///
/// Convention: each module that hosts projectors owns one inbox table
/// (`projection_inbox`) inside its own DbContext. Sharing across modules
/// is forbidden — it would couple unrelated transactional boundaries.
/// </summary>
public class InboxMessage
{
    /// <summary>
    /// Composite uniqueness — same EventId may be consumed by multiple
    /// projectors (each gets its own row). Surrogate PK keeps the table
    /// EF-friendly; the UNIQUE constraint is on (ProjectorName, EventId).
    /// </summary>
    public Guid Id { get; private set; }

    public string ProjectorName { get; private set; } = string.Empty;

    public Guid EventId { get; private set; }

    public DateTime ProcessedAtUtc { get; private set; }

    private InboxMessage() { }   // EF

    public InboxMessage(string projectorName, Guid eventId, DateTime processedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(projectorName))
            throw new ArgumentException("ProjectorName is required.", nameof(projectorName));
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId must not be empty.", nameof(eventId));

        Id = Guid.NewGuid();
        ProjectorName = projectorName;
        EventId = eventId;
        ProcessedAtUtc = processedAtUtc;
    }
}
