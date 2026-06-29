namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// Phase S.3.1b — declares that a <see cref="SystemClient"/> wants
/// outbound callbacks for a specific integration event type, formatted
/// by the named payload formatter.
///
/// <para>One row per <c>(SystemKey, EventType)</c> tuple — the unique
/// constraint at the DB level enforces "a system subscribes to a given
/// event type at most once". Disable via <see cref="Disable"/> rather
/// than deleting to keep audit continuity; re-enable via
/// <see cref="Enable"/>.</para>
/// </summary>
public sealed class SystemEventSubscription
{
    public Guid Id { get; private set; }
    public string SystemKey { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string PayloadFormatKey { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private SystemEventSubscription() { }

    public SystemEventSubscription(
        Guid id,
        string systemKey,
        string eventType,
        string payloadFormatKey,
        bool enabled = true)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("SystemKey is required.", nameof(systemKey));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(payloadFormatKey))
            throw new ArgumentException("PayloadFormatKey is required.", nameof(payloadFormatKey));

        Id = id;
        SystemKey = systemKey;
        EventType = eventType;
        PayloadFormatKey = payloadFormatKey;
        Enabled = enabled;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public void Enable()
    {
        if (Enabled) return;
        Enabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Disable()
    {
        if (!Enabled) return;
        Enabled = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdatePayloadFormatKey(string payloadFormatKey)
    {
        if (string.IsNullOrWhiteSpace(payloadFormatKey))
            throw new ArgumentException("PayloadFormatKey is required.", nameof(payloadFormatKey));
        if (PayloadFormatKey == payloadFormatKey) return;
        PayloadFormatKey = payloadFormatKey;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
