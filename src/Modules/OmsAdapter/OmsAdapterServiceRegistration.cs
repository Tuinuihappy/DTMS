using AMR.DeliveryPlanning.OmsAdapter.Abstractions;
using AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Options;
using AMR.DeliveryPlanning.OmsAdapter.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.OmsAdapter;

public static class OmsAdapterServiceRegistration
{
    public static IServiceCollection AddOmsAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(UpstreamOmsOptions.SectionName);
        services.Configure<UpstreamOmsOptions>(section);

        var options = section.Get<UpstreamOmsOptions>() ?? new UpstreamOmsOptions();
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "http://localhost/" : options.BaseUrl;

        services.AddHttpClient<IOmsShipmentClient, HttpOmsShipmentClient>((sp, client) =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 10);

            if (!string.IsNullOrWhiteSpace(options.BearerToken))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Authorization", $"Bearer {options.BearerToken}");
            }
            else
            {
                // Dev/test setups commonly run without a token. Log once so it's
                // obvious why upstream rejects with 401 instead of silent failure.
                var logger = sp.GetRequiredService<ILogger<HttpOmsShipmentClient>>();
                logger.LogWarning("[OmsAdapter] UpstreamOms:BearerToken not configured — requests will be sent without Authorization header.");
            }
        });

        return services;
    }
}
