using System.Security.Cryptography;
using System.Text;
using DTMS.Planning.Domain.Enums;
using DTMS.SharedKernel.Domain;

namespace DTMS.Planning.Domain.Entities;

// Records ONE manual dispatch attempt of an OrderTemplate (POST
// /order-templates/{id}/create) so a repeat of the SAME operator intent
// can be recognised and de-duplicated before we call the vendor again.
//
// Why this exists at all: RIOT3 does NOT de-duplicate on upperKey — the
// same key produces a brand new robot order every time. The guard has to
// be entirely on our side, and it has to be authoritative BEFORE the
// vendor call, so the row is committed first and updated after.
//
// Scope is deliberately narrow. The /create path is fire-and-forget by
// design (no Trip, no webhook callbacks, no reconciliation) — this entity
// tracks OUR ACTION ("we sent this command"), never the ORDER's lifecycle.
// That is why it does not reuse dispatch.Trips, which would drag the whole
// tracking machinery in.
//
// Uniqueness is on IdempotencyKey alone — never on OrderTemplateId.
// Dispatching the same template repeatedly is normal operation (e.g. three
// trips of the same route) and must never be blocked.
public class DispatchClaim : AggregateRoot<Guid>
{
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid OrderTemplateId { get; private set; }
    // Correlation key handed to the vendor. Derived from IdempotencyKey so a
    // retry that slips past the guard is at least detectable (same upperKey
    // appearing twice), and so an in-doubt attempt can be resolved by asking
    // the vendor whether this key exists.
    public string UpperKey { get; private set; } = string.Empty;
    public DispatchClaimStatus Status { get; private set; } = DispatchClaimStatus.InProgress;
    public string? VendorOrderKey { get; private set; }
    // Hash of the request payload. Same key + different payload is a caller
    // mistake (e.g. priority edited then re-submitted) and is rejected rather
    // than silently replaying the original.
    public string RequestHash { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private DispatchClaim() { } // EF Core

    public DispatchClaim(string idempotencyKey, Guid orderTemplateId, string requestHash)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("idempotencyKey required.", nameof(idempotencyKey));

        Id = Guid.NewGuid();
        IdempotencyKey = idempotencyKey.Trim();
        OrderTemplateId = orderTemplateId;
        RequestHash = requestHash;
        UpperKey = DeriveUpperKey(IdempotencyKey);
        Status = DispatchClaimStatus.InProgress;
        CreatedAt = DateTime.UtcNow;
    }

    // SHA-256 → lowercase hex, truncated to 32 chars. Bounded length and
    // hex-only charset keep it safe for any vendor field limit (an
    // Idempotency-Key may be up to 200 chars, which could exceed one).
    public static string DeriveUpperKey(string idempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey.Trim()));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    public void MarkSucceeded(string? vendorOrderKey)
    {
        Status = DispatchClaimStatus.Succeeded;
        VendorOrderKey = vendorOrderKey;
        CompletedAt = DateTime.UtcNow;
    }

    // Only for failures we KNOW did not create an order (vendor rejected the
    // request). An unknown/timed-out attempt must stay InProgress — see the
    // in-doubt resolution in the instantiate handler.
    public void MarkFailed(string? reason)
    {
        Status = DispatchClaimStatus.Failed;
        FailureReason = Truncate(reason, 1000);
        CompletedAt = DateTime.UtcNow;
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
