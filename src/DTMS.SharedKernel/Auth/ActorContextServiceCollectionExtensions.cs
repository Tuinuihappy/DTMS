using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AMR.DeliveryPlanning.SharedKernel.Auth;

public static class ActorContextServiceCollectionExtensions
{
    /// <summary>
    /// Wire the default <see cref="ICurrentActorContext"/> implementation.
    /// Pass <paramref name="httpResolver"/> from the host so HTTP requests
    /// resolve to a context populated from <c>HttpContext.User</c>; non-HTTP
    /// callers (consumers, background services) fall back to ambient scope
    /// or <see cref="ActorContext.System"/>.
    /// </summary>
    public static IServiceCollection AddActorContext(
        this IServiceCollection services,
        Func<IServiceProvider, Func<ActorContext?>>? httpResolverFactory = null)
    {
        services.TryAddSingleton<ICurrentActorContext>(sp =>
        {
            var resolver = httpResolverFactory?.Invoke(sp);
            return new AsyncLocalActorContext(resolver);
        });
        return services;
    }
}
