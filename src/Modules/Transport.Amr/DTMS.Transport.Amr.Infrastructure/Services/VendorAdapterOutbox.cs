using DTMS.SharedKernel.Domain;
using DTMS.SharedKernel.Outbox;
using AMR.DeliveryPlanning.Transport.Abstractions.Services;
using AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Data;

namespace AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Services;

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
