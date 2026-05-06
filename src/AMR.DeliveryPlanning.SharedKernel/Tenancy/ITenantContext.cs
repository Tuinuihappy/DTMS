namespace AMR.DeliveryPlanning.SharedKernel.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
    string? UserId { get; }
    bool HasTenant => TenantId != Guid.Empty;
}
