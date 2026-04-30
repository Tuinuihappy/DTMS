using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AMR.DeliveryPlanning.Api.Auth;

public sealed class DevAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAuth";

    public DevAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenantId = Context.RequestServices
            .GetRequiredService<IConfiguration>()
            .GetValue<Guid?>("Auth:DevTenantId")
            ?? AppUser.SystemTenantId;

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "dev"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.NameIdentifier, "dev-auth-bypass"),
            new Claim("tenant_id", tenantId.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
