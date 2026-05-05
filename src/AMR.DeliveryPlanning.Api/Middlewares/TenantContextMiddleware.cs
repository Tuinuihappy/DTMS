using System.Security.Claims;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;

namespace AMR.DeliveryPlanning.Api.Middlewares;

public sealed class TenantContextMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var tenantClaim = context.User.FindFirstValue("tenant_id");

        if (tenantClaim != null && Guid.TryParse(tenantClaim, out var tenantId))
            tenantContext.Set(tenantId);
        else if (Guid.TryParse(configuration["Tenancy:DefaultTenantId"], out var defaultId))
            tenantContext.Set(defaultId);

        await next(context);
    }
}
