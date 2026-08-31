using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Domain.Entities;

/// <summary>
/// One maintenance episode for a carrier — opened when it goes out of service,
/// closed when it comes back (ADR-019 §maintenance).
///
/// <para><b>Why a log and not a <see cref="MaintenanceRecord"/>.</b> Carrier
/// repair is reactive: something breaks, the cart is pulled. There is no
/// scheduling, which is the whole reason <see cref="MaintenanceRecord"/> exists
/// for vehicles (ScheduledAt + MaintenanceType.Scheduled). What is genuinely
/// irrecoverable is the history — if it is not written from day one it can never
/// be reconstructed — so this captures exactly that and nothing more. Upgrading
/// to a full record later is additive.</para>
///
/// <para>At most one row per carrier may be open at a time, enforced by a
/// partial unique index on <c>(CarrierId) WHERE EndedAt IS NULL</c> rather than
/// by application checks.</para>
/// </summary>
public class CarrierMaintenanceLog : Entity<Guid>
{
    public Guid CarrierId { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    public DateTime StartedAt { get; private set; }
    public string StartedBy { get; private set; } = string.Empty;

    public DateTime? EndedAt { get; private set; }
    public string? EndedBy { get; private set; }
    public string? Outcome { get; private set; }

    public bool IsOpen => EndedAt is null;

    private CarrierMaintenanceLog() { }

    public CarrierMaintenanceLog(Guid carrierId, string reason, string startedBy)
    {
        if (carrierId == Guid.Empty)
            throw new ArgumentException("CarrierId must not be empty.", nameof(carrierId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        Id = Guid.NewGuid();
        CarrierId = carrierId;
        Reason = reason.Trim();
        StartedAt = DateTime.UtcNow;
        // Never empty: the caller resolves DisplayName → PrincipalId → "system"
        // before getting here. A NOT NULL column accepts "" happily and leaves a
        // history row that says nothing.
        StartedBy = string.IsNullOrWhiteSpace(startedBy) ? "system" : startedBy.Trim();
    }

    public void End(string? outcome, string? endedBy)
    {
        if (!IsOpen)
            throw new InvalidOperationException("Maintenance episode is already closed.");

        EndedAt = DateTime.UtcNow;
        EndedBy = string.IsNullOrWhiteSpace(endedBy) ? "system" : endedBy.Trim();
        Outcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim();
    }
}
