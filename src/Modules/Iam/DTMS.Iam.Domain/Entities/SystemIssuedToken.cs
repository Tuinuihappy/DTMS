namespace DTMS.Iam.Domain.Entities;

/// <summary>
/// Phase S.8c — audit record of every admin-issued long-lived system JWT.
/// Row is inserted on <c>POST /api/v1/iam/systems/{key}/issue-token</c>
/// and marked <see cref="Status"/>=<c>Revoked</c> when admin clicks Revoke.
/// The revocation list itself lives in Redis (fast per-request lookup);
/// this table is the durable audit trail + UI backing store.
///
/// <para><b>Why we don't store the JWT itself.</b> Only the <see cref="Jti"/>
/// (unique per-token id) + metadata — never the token body or its
/// signature. Admin issued it once, partner holds it now. Storing it
/// again would double the leak surface for zero operational benefit.</para>
///
/// <para><b>Why the row lives past expiry.</b> Audit history: "who did
/// admin issue tokens to, when, for what purpose". A background job
/// (future) may prune rows past exp + N days for storage; MVP keeps
/// forever since the row is small.</para>
/// </summary>
public sealed class SystemIssuedToken
{
    public Guid Id { get; private set; }
    public string SystemKey { get; private set; } = string.Empty;

    /// <summary>The JWT's <c>jti</c> claim — unique id per mint.
    /// Redis blocklist keys on this value; validator checks it every
    /// request when the token's scheme is bearer-jwt.</summary>
    public string Jti { get; private set; } = string.Empty;

    public DateTime IssuedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    /// <summary>Employee id (or "unknown") of the admin who clicked Issue.
    /// Read from ctx.User.Sub at the endpoint — no separate write path.</summary>
    public string IssuedBy { get; private set; } = string.Empty;

    public SystemIssuedTokenStatus Status { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? RevokedBy { get; private set; }
    public string? RevokeReason { get; private set; }

    private SystemIssuedToken() { }

    public SystemIssuedToken(
        Guid id,
        string systemKey,
        string jti,
        DateTime issuedAt,
        DateTime expiresAt,
        string issuedBy)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id required.", nameof(id));
        if (string.IsNullOrWhiteSpace(systemKey))
            throw new ArgumentException("SystemKey required.", nameof(systemKey));
        if (string.IsNullOrWhiteSpace(jti))
            throw new ArgumentException("Jti required.", nameof(jti));
        if (expiresAt <= issuedAt)
            throw new ArgumentException("ExpiresAt must be after IssuedAt.", nameof(expiresAt));
        if (string.IsNullOrWhiteSpace(issuedBy))
            throw new ArgumentException("IssuedBy required.", nameof(issuedBy));

        Id = id;
        SystemKey = systemKey;
        Jti = jti;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
        IssuedBy = issuedBy;
        Status = SystemIssuedTokenStatus.Active;
    }

    /// <summary>Mark this token as revoked. Idempotent — repeated calls
    /// are no-ops (first revoker wins on the audit fields).</summary>
    public void Revoke(string revokedBy, string? reason = null)
    {
        if (Status == SystemIssuedTokenStatus.Revoked) return;
        if (string.IsNullOrWhiteSpace(revokedBy))
            throw new ArgumentException("RevokedBy required.", nameof(revokedBy));

        Status = SystemIssuedTokenStatus.Revoked;
        RevokedAt = DateTime.UtcNow;
        RevokedBy = revokedBy;
        RevokeReason = reason;
    }
}

/// <summary>Stored as string in DB for readable admin queries + easy
/// migration when we add Expired as a third bucket down the road.</summary>
public enum SystemIssuedTokenStatus
{
    Active = 0,
    Revoked = 1,
}
