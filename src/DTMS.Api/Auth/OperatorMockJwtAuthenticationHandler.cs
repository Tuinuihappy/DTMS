using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Api.Auth;

// Phase 4.2 — Mock JWT validator for the operator PWA.
//
// IMPORTANT: This handler validates JWT STRUCTURE only — it does NOT
// verify the signature. It exists so the operator-side codebase can be
// built and exercised end-to-end before the External Auth team (per
// ADR-014) supplies JWKS endpoint + signing algorithm + token lifetime.
//
// When that information arrives, swap to JwtBearerHandler with the
// real signing key (see the existing Program.cs default scheme for the
// template). Until then this scheme is enabled only in development
// builds via TransportManual:OperatorAuth:MockMode = true.
//
// Claims extracted (matching the screenshot of External Auth's response):
//   sub          → employeeCode (the canonical operator identifier)
//   employeeCode → fallback if 'sub' is missing (External Auth uses this name)
//   name         → displayName
//   role         → role (Operator/Supervisor/Admin)
//
// The actual upsert into DTMS's Operator table happens downstream in
// OperatorSyncMiddleware; this handler's only job is to populate
// ClaimsPrincipal so [Authorize] + the middleware see consistent data.
public sealed class OperatorMockJwtAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "OperatorJwt";

    public OperatorMockJwtAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return Task.FromResult(AuthenticateResult.Fail("Empty bearer token."));

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            return Task.FromResult(AuthenticateResult.Fail("Bearer token is not a valid JWT."));

        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(token);
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail($"Failed to read JWT: {ex.Message}"));
        }

        // Honour exp claim if present — mock mode skips signature checks
        // but expired tokens should still be rejected so devs notice.
        if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < DateTime.UtcNow)
            return Task.FromResult(AuthenticateResult.Fail("Token has expired."));

        // DTMS receives JWTs from three sources, each with its own claim
        // convention. The handler tries each in order so any of them works
        // without forcing the issuer to align on a specific name:
        //   - JWT standard:           "sub"
        //   - DTMS internal API:      "employeeCode"
        //   - External Auth + frontend dev bypass:
        //       "EmployeeId" + the long ClaimTypes URIs that .NET emits
        //       via System.Security.Claims.ClaimTypes.NameIdentifier
        const string NameIdUri = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
        const string NameUri = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";
        const string RoleUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

        var employeeCode = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                         ?? jwt.Claims.FirstOrDefault(c => c.Type == "employeeCode")?.Value
                         ?? jwt.Claims.FirstOrDefault(c => c.Type == "EmployeeId")?.Value
                         ?? jwt.Claims.FirstOrDefault(c => c.Type == NameIdUri)?.Value;
        if (string.IsNullOrWhiteSpace(employeeCode))
            return Task.FromResult(AuthenticateResult.Fail(
                "Token missing 'sub' / 'employeeCode' / 'EmployeeId' claim."));

        var displayName = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value
                       ?? jwt.Claims.FirstOrDefault(c => c.Type == "displayName")?.Value
                       ?? jwt.Claims.FirstOrDefault(c => c.Type == NameUri)?.Value
                       ?? employeeCode;
        var role = jwt.Claims.FirstOrDefault(c => c.Type == "role")?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == RoleUri)?.Value
                ?? "Operator";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, employeeCode),
            new("employeeCode", employeeCode),
            new(ClaimTypes.Name, displayName),
            new("displayName", displayName),
            new(ClaimTypes.Role, role),
            new("role", role),
        };
        // Pass through warehouseId / thumbnailUrl if External Auth supplied them.
        foreach (var passthrough in new[] { "warehouseId", "primaryWarehouseId", "thumbnailUrl", "phone" })
        {
            var v = jwt.Claims.FirstOrDefault(c => c.Type == passthrough)?.Value;
            if (!string.IsNullOrWhiteSpace(v)) claims.Add(new Claim(passthrough, v));
        }

        Logger.LogWarning(
            "OperatorJwt MOCK MODE — accepting token for {EmployeeCode} ({Role}) without signature verification. Swap to real JWKS validation before production.",
            employeeCode, role);

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
