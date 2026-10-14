using System.Security.Claims;

namespace DTMS.Api.Auth;

/// <summary>
/// How an operator's identity is read from a token — shared by
/// <see cref="OperatorSyncMiddleware"/> (which stores it as
/// Operator.EmployeeCode) and the operator hub (which groups connections by
/// it), so the two always agree on who a connection belongs to.
/// </summary>
public static class OperatorClaims
{
    /// <summary>
    /// External Auth JWTs carry the employee identity under <c>EmployeeId</c>
    /// (short custom claim per ADR-014). Falls back to <c>employeeCode</c>
    /// (dev fixture) or the subject so unit tests + hand-rolled tokens keep
    /// working. <c>MapInboundClaims=false</c> on the JwtBearer options means
    /// <c>sub</c> stays as-is — hence that fallback before NameIdentifier.
    /// </summary>
    public static string? EmployeeCode(ClaimsPrincipal? user)
    {
        if (user is null) return null;
        var code = user.FindFirstValue("EmployeeId")
            ?? user.FindFirstValue("employeeCode")
            ?? user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(code) ? null : code;
    }
}
