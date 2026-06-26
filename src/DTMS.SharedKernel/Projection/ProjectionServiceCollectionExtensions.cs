using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DTMS.SharedKernel.Projection;

public static class ProjectionServiceCollectionExtensions
{
    /// <summary>
    /// Register the singletons + stub services that every projector in
    /// DTMS depends on. Idempotent — safe to call multiple times during
    /// module registration. Per-module pieces (IProjectionInboxRepository
    /// implementations + the inbox EF entity) are wired in each module's
    /// own infrastructure registration, since they own their own DbContext.
    /// </summary>
    public static IServiceCollection AddProjectionFoundation(this IServiceCollection services)
    {
        services.TryAddSingleton<ProjectionMetrics>();
        services.TryAddSingleton<IProjectionReplayService, NotImplementedReplayService>();
        return services;
    }
}
