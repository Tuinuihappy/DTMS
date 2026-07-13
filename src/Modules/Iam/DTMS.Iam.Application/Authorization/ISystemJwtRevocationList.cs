namespace DTMS.Iam.Application.Authorization;

/// <summary>
/// Phase S.8c — per-jti revocation list for admin-issued long-lived
/// system JWTs. Backed by Redis for hot-path validator lookup + auto
/// TTL cleanup (the Redis key expires at the same time as the token,
/// so revocation state costs nothing after the natural expiry).
///
/// <para><b>Fail-close semantics.</b> If Redis is unreachable during
/// <see cref="IsRevokedAsync"/>, the implementation THROWS — the
/// validator wraps this in a try/catch and returns "reject" (401).
/// Alternative (fail-open) was considered and explicitly rejected: a
/// revoked-but-leaked token slipping through a Redis outage undoes the
/// point of having revocation. Downtime of the /source pipe when Redis
/// is down is an acceptable trade for a security control that actually
/// controls.</para>
/// </summary>
public interface ISystemJwtRevocationList
{
    /// <summary>Add a jti to the blocklist. TTL is set to the token's
    /// remaining lifetime — after natural expiry the key drops on its
    /// own (Redis EXPIRE), and the audit row in the DB carries the
    /// long-term history. Pass <c>null</c> for a perpetual token (Phase
    /// S.8d): the key is written with NO expiry so the blocklist entry
    /// never drops on its own — the durable DB allowlist is the source of
    /// truth if Redis is later flushed.</summary>
    Task RevokeAsync(string jti, DateTime? expiresAt, CancellationToken ct = default);

    /// <summary>Check if a jti is on the blocklist. Throws
    /// <see cref="System.Net.WebException"/> or Redis-specific
    /// exceptions if the backing store is unreachable — the validator
    /// treats any exception as fail-close (reject the request).</summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default);
}
