using DTMS.OmsAdapter.Abstractions;
using DTMS.OmsAdapter.Infrastructure.Options;
using DTMS.OmsAdapter.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DTMS.OmsAdapter;

public static class OmsAdapterServiceRegistration
{
    public static IServiceCollection AddOmsAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        // UpstreamOmsOptions is kept as a backward-compat fallback. The
        // OmsCallbackTargetResolver tries iam.SystemCredentials.CallbackBaseUrl
        // first, then falls back to UpstreamOms__BaseUrl env. Ops can move
        // config from env to UI without redeploying.
        var section = configuration.GetSection(UpstreamOmsOptions.SectionName);
        services.Configure<UpstreamOmsOptions>(section);

        services.AddScoped<IOmsCallbackTargetResolver, OmsCallbackTargetResolver>();

        // HttpClient with NO fixed BaseAddress / Authorization. Each call
        // builds its own request with the resolved target's URL + token,
        // so a UI rotation propagates without container restart. Top-level
        // timeout is generous; per-call deadlines come from the resolved
        // target's Timeout (read out of SystemCredential.CallbackTimeoutMs).
        services.AddHttpClient<IOmsShipmentClient, HttpOmsShipmentClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
