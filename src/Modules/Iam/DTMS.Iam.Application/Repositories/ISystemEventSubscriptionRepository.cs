using DTMS.Iam.Domain.Entities;

namespace DTMS.Iam.Application.Repositories;

/// <summary>
/// Persistence contract for <see cref="SystemEventSubscription"/>.
/// Read-side hot path is fronted by
/// <see cref="DTMS.Iam.Application.Callbacks.ISubscriptionLookup"/> —
/// admin CRUD goes through this interface directly so audit + Redis
/// invalidation happen on the writer's scope.
/// </summary>
public interface ISystemEventSubscriptionRepository
{
    Task<SystemEventSubscription?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<SystemEventSubscription?> GetAsync(string systemKey, string eventType, CancellationToken ct = default);

    Task<IReadOnlyList<SystemEventSubscription>> ListBySystemAsync(string systemKey, CancellationToken ct = default);

    /// <summary>
    /// Enabled subscriptions for a given event type — drives the fan-out
    /// producer's lookup. Cache layer above this caps DB pressure.
    /// </summary>
    Task<IReadOnlyList<SystemEventSubscription>> ListEnabledByEventTypeAsync(string eventType, CancellationToken ct = default);

    Task AddAsync(SystemEventSubscription subscription, CancellationToken ct = default);
    Task UpdateAsync(SystemEventSubscription subscription, CancellationToken ct = default);
    Task RemoveAsync(SystemEventSubscription subscription, CancellationToken ct = default);
}
