using System.Security.Claims;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.Api.Middlewares;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext)
    {
        var tenantClaim = context.User.FindFirstValue("tenant_id");
        if (tenantClaim != null && Guid.TryParse(tenantClaim, out var tenantId))
            tenantContext.Set(tenantId);

        await next(context);
    }
}
