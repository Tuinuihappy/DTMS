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
    /// </summary>
    public static IServiceCollection AddDtmsTieredCache(this IServiceCollection services)
    {
        services.AddMemoryCache();
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
    public static IServiceCollection AddDtmsBatchedLog<T>(
        this IServiceCollection services,
        Action<BatchedLogWriterOptions<T>>? configure = null)
    {
        var opts = new BatchedLogWriterOptions<T>();
        configure?.Invoke(opts);
        services.AddSingleton(opts);
        services.AddSingleton<BatchedLogWriter<T>>();
        services.AddSingleton<IBatchedLogWriter<T>>(sp => sp.GetRequiredService<BatchedLogWriter<T>>());
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
