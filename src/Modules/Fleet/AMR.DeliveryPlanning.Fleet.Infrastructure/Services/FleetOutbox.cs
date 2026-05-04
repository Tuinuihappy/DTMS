using AMR.DeliveryPlanning.Fleet.Application.Services;
using AMR.DeliveryPlanning.Fleet.Infrastructure.Data;
using AMR.DeliveryPlanning.SharedKernel.Domain;
using AMR.DeliveryPlanning.SharedKernel.Outbox;

namespace AMR.DeliveryPlanning.Fleet.Infrastructure.Services;

public class FleetOutbox : IFleetOutbox
{
    private readonly FleetDbContext _db;

    public FleetOutbox(FleetDbContext db)
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
