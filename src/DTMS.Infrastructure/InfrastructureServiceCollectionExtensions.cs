using DTMS.Infrastructure.Caching;
using DTMS.Infrastructure.Database;
using DTMS.Infrastructure.Logging;
using DTMS.Infrastructure.Resilience;
using DTMS.SharedKernel.Caching;
using DTMS.SharedKernel.Logging;
using DTMS.SharedKernel.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace DTMS.Infrastructure;

/// <summary>
/// Composition-root extension methods for the shared scale infrastructure
/// introduced in Phase S.0. Idempotent — calling the cache or
/// circuit-breaker registration twice resolves to the same singleton.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITieredCache"/> + the cross-pod invalidation
    /// subscriber. Requires <see cref="IConnectionMultiplexer"/> already
    /// registered. <see cref="IMemoryCache"/> is added if absent.
    /// <see cref="PodIdentity"/> is added as a singleton so the cache
    /// writer and subscriber agree on the sender id used to filter local
    /// echoes from peer-published invalidations.
    /// </summary>
    public static IServiceCollection AddDtmsTieredCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<PodIdentity>();
        services.AddSingleton<ITieredCache, RedisBackedTieredCache>();
        services.AddHostedService<CacheInvalidationSubscriber>();
        return services;
    }

    /// <summary>
    /// Registers the Redis-backed distributed circuit breaker. Requires
    /// <see cref="IConnectionMultiplexer"/> already registered.
    /// </summary>
    public static IServiceCollection AddDtmsDistributedCircuitBreaker(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedCircuitBreaker, RedisDistributedCircuitBreaker>();
        return services;
    }

    /// <summary>
    /// Registers a typed batched-log writer and its drain service. The
    /// owning module must register an <see cref="IBatchedLogSink{T}"/>
    /// implementation separately — without one the drain service starts
    /// but every flush will throw.
    /// </summary>
    /// <remarks>
    /// Idempotent per <typeparamref name="T"/>: calling this method
    /// twice for the same entry type is a no-op on the second call.
    /// Without the guard, two modules registering the same T would
    /// install two <see cref="BatchedLogDrainService{T}"/> instances
    /// racing TryRead on a channel marked <c>SingleReader=true</c> —
    /// undefined behaviour.
    /// </remarks>
    public static IServiceCollection AddDtmsBatchedLog<T>(
        this IServiceCollection services,
        Action<BatchedLogWriterOptions<T>>? configure = null)
    {
        // Probe by writer singleton — if it's there, the trio (options +
        // writer + drain) was already installed by a prior call.
        bool alreadyRegistered = false;
        foreach (var d in services)
        {
            if (d.ServiceType == typeof(BatchedLogWriter<T>))
            {
                alreadyRegistered = true;
                break;
            }
        }
        if (alreadyRegistered)
            return services;

        var opts = new BatchedLogWriterOptions<T>();
        configure?.Invoke(opts);
        services.TryAddSingleton(opts);
        services.TryAddSingleton<BatchedLogWriter<T>>();
        services.TryAddSingleton<IBatchedLogWriter<T>>(sp => sp.GetRequiredService<BatchedLogWriter<T>>());
        services.AddHostedService<BatchedLogDrainService<T>>();
        return services;
    }

    /// <summary>
    /// Registers a partition-maintenance background service for the
    /// given <typeparamref name="TContext"/>. The owning module must
    /// register <c>AddDbContextFactory&lt;TContext&gt;()</c> first; this
    /// helper asserts the registration up-front so a misconfiguration
    /// surfaces at <c>Program.cs</c> wiring time rather than at host
    /// startup with a less obvious "cannot resolve IDbContextFactory"
    /// stack trace.
    /// </summary>
    public static IServiceCollection AddDtmsPartitionMaintenance<TContext>(
        this IServiceCollection services,
        Action<PartitionMaintenanceOptions<TContext>> configure)
        where TContext : DbContext
    {
        bool factoryRegistered = false;
        foreach (var d in services)
        {
            if (d.ServiceType == typeof(IDbContextFactory<TContext>))
            {
                factoryRegistered = true;
                break;
            }
        }
        if (!factoryRegistered)
        {
            throw new InvalidOperationException(
                $"AddDtmsPartitionMaintenance<{typeof(TContext).Name}> requires " +
                $"AddDbContextFactory<{typeof(TContext).Name}>() to be registered first. " +
                "DTMS modules historically register AddDbContext (scoped) only; the " +
                "partition service uses a factory so its background loop can own its " +
                "own DbContext lifetime independent of any request scope.");
        }

        services.Configure(configure);
        services.AddHostedService<PartitionMaintenanceService<TContext>>();
        return services;
    }
}
