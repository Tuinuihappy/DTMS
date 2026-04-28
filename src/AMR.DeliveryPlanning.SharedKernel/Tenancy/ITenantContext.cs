namespace AMR.DeliveryPlanning.SharedKernel.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant => TenantId != Guid.Empty;
}
