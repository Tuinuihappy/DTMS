using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Data;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;

public class VendorAdapterOutbox : IVendorAdapterOutbox
{
    private readonly VendorAdapterDbContext _db;

    public VendorAdapterOutbox(VendorAdapterDbContext db)
    {
        _db = db;
    }

    public Task AddAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent
    {
        _db.OutboxMessages.Add(OutboxMessageFactory.FromIntegrationEvent(integrationEvent));
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
