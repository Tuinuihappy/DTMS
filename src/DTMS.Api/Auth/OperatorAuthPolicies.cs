using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace DTMS.Api.Auth;

// Authorization policies for the operator PWA endpoints.
// Roles come from the External Auth JWT's 'role' claim (per ADR-014).
// All schemes use the default JwtBearer handler with RSA signature
// validation against the External Auth public key.
public static class OperatorAuthPolicies
{
    public const string OperatorOnly = "OperatorOnly";
    public const string SupervisorOnly = "SupervisorOnly";
    public const string AdminOnly = "AdminOnly";

    public static AuthorizationOptions AddOperatorPolicies(this AuthorizationOptions o)
    {
        // Operator endpoints (the bulk of /api/operator/*) — any of the
        // three roles can act on their own assigned trips. Supervisor
        // and Admin can additionally approve overrides + see operator
        // history (those endpoints layer the stricter policies below).
        o.AddPolicy(OperatorOnly, p => p
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole("Operator", "Supervisor", "Admin"));

        o.AddPolicy(SupervisorOnly, p => p
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole("Supervisor", "Admin"));

        o.AddPolicy(AdminOnly, p => p
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireRole("Admin"));

        return o;
    }
}
