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
    /// Optional email address. Not every employee has one — always handle
    /// the null case. Never use as a lookup key.
    /// </summary>
    public static string? GetEmail(this ClaimsPrincipal user)
        => user.FindFirst("Email")?.Value;
}
