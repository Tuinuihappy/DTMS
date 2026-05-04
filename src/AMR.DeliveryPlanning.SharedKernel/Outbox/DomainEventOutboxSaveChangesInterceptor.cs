using AMR.DeliveryPlanning.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AMR.DeliveryPlanning.SharedKernel.Outbox;

public class DomainEventOutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventToIntegrationEventMapper _mapper;

    public DomainEventOutboxSaveChangesInterceptor(IDomainEventToIntegrationEventMapper mapper)
    {
        _mapper = mapper;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddOutboxMessages(DbContext? db)
    {
        if (db == null) return;

        var aggregates = db.ChangeTracker
            .Entries()
            .Select(e => e.Entity)
            .OfType<IAggregateRoot>()
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        if (aggregates.Count == 0) return;

        var trackedOutboxIds = db.ChangeTracker
            .Entries<OutboxMessage>()
            .Select(e => e.Entity.Id)
            .ToHashSet();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                foreach (var integrationEvent in _mapper.Map(domainEvent))
                {
                    if (!trackedOutboxIds.Add(integrationEvent.EventId))
                        continue;

                    db.Set<OutboxMessage>().Add(OutboxMessageFactory.FromIntegrationEvent(integrationEvent));
                }
            }

            aggregate.ClearDomainEvents();
        }
    }
}
