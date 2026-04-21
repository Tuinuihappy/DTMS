using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Extensions;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;
using AMR.DeliveryPlanning.VendorAdapter.Simulator.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure;

public static class VendorAdapterServiceRegistration
{
    public static IServiceCollection AddVendorAdapterInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Register HttpClient for RIOT 3.0 with Polly resilience
        services.AddHttpClient("Riot3", client =>
        {
            var baseUrl = configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:5100";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Register command services
        services.AddScoped<Riot3CommandService>();
        services.AddScoped<SimulatorCommandService>();

        // Register the adapter factory
        services.AddScoped<IVendorAdapterFactory, VendorAdapterFactory>();

        return services;
    }
}
