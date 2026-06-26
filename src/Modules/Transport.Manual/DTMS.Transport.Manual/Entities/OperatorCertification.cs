using AMR.DeliveryPlanning.Transport.Manual.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Manual.Domain.Entities;

// Phase 4.1 — One certification record per (Operator, CertificationType)
// pair. Revoked certs stay in the table for audit (compliance often wants
// to see "operator X had hazmat clearance from date A to date B").
// IsActive flips false on Revoke; the assignment policy filters on active
// certs only.
public class OperatorCertification
{
    public Guid Id { get; private set; }
    public Guid OperatorId { get; private set; }
    public CertificationType Type { get; private set; }
    public DateTime IssuedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedReason { get; private set; }

    private OperatorCertification() { }

    internal static OperatorCertification Create(
        Guid operatorId, CertificationType type, DateTime? expiresAt)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("OperatorId must not be empty.", nameof(operatorId));
        if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
            throw new ArgumentException("ExpiresAt must be in the future.", nameof(expiresAt));

        return new OperatorCertification
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorId,
            Type = type,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            IsActive = true,
        };
    }

    internal void Revoke(string reason)
    {
        if (!IsActive) return;
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Revocation reason must not be empty.", nameof(reason));
        IsActive = false;
        RevokedAt = DateTime.UtcNow;
        RevokedReason = reason.Trim();
    }

    // Used by assignment policy — "can this operator handle hazmat cargo?"
    public bool IsCurrentlyValid(DateTime asOf)
    {
        if (!IsActive) return false;
        if (ExpiresAt.HasValue && ExpiresAt.Value <= asOf) return false;
        return true;
    }
}
