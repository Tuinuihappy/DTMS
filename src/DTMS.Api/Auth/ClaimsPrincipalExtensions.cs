using System.Security.Claims;

namespace DTMS.Api.Auth;

/// <summary>
/// Strongly-typed accessors for the External Auth JWT claims DTMS cares
/// about. Per ADR-014, EmployeeId is the canonical identity and is always
/// present; Email is optional (some employees have no email on file).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Stable employee identifier (e.g. "86347852"). Present on every
    /// authenticated request. Prefer this over <see cref="ClaimsIdentity.Name"/>
    /// (username) for foreign keys and audit stamps.
    /// </summary>
    public static string? GetEmployeeId(this ClaimsPrincipal user)
        => user.FindFirst("EmployeeId")?.Value;

    /// <summary>
    /// The id a user is known by in the audit trail and for their own rate-limit
    /// quota — one chain, so the two can never disagree about who someone is.
    /// May return null or whitespace when the token names nobody; callers treat
    /// both as "no user".
    ///
    /// <para>Real External Auth LDAP tokens carry none of the first three claims
    /// (decoded live 2026-07-23): identity is in <c>sub</c>, the employee id.
    /// Only the old dev-bypass JWT shipped <c>EmployeeId</c>. Without the
    /// <c>sub</c> fallback every audit row since the bypass was disabled stamped
    /// TriggeredBy="http". System JWTs use <c>sub = "system:{key}"</c>; they are
    /// identified elsewhere, but are refused here so one can never pass as a
    /// user id if that path is skipped.</para>
    /// </summary>
    public static string? ResolveUserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst("EmployeeId")?.Value
              ?? user.FindFirst("employeeCode")?.Value
              ?? user.Identity?.Name;

        if (string.IsNullOrWhiteSpace(id))
        {
            var sub = user.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(sub)
                && !sub.StartsWith("system:", StringComparison.Ordinal))
                id = sub;
        }

        return id;
    }

    /// <summary>
    /// Optional email address. Not every employee has one — always handle
    /// the null case. Never use as a lookup key.
    /// </summary>
    public static string? GetEmail(this ClaimsPrincipal user)
        => user.FindFirst("Email")?.Value;
}
