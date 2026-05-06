using System.Security.Claims;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;

namespace AMR.DeliveryPlanning.Api.Middlewares;

public sealed class TenantContextMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var tenantClaim = context.User.FindFirstValue("tenant_id");
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");

        if (tenantClaim != null && Guid.TryParse(tenantClaim, out var tenantId))
            tenantContext.Set(tenantId, userId);
        else if (Guid.TryParse(configuration["Tenancy:DefaultTenantId"], out var defaultId))
            tenantContext.Set(defaultId, userId);

        await next(context);
    }
}
