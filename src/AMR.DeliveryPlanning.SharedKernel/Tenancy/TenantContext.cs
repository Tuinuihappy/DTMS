namespace AMR.DeliveryPlanning.SharedKernel.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; } = Guid.Empty;

    // Called once per request by TenantContextMiddleware — not intended for application code.
    public void Set(Guid tenantId) => TenantId = tenantId;
}
