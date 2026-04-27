using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;
using AMR.DeliveryPlanning.VendorAdapter.Feeder.Services;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Extensions;
using AMR.DeliveryPlanning.VendorAdapter.Infrastructure.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;
using AMR.DeliveryPlanning.VendorAdapter.Simulator.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.VendorAdapter.Infrastructure;

public static class VendorAdapterServiceRegistration
{
    public static IServiceCollection AddVendorAdapterInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var riot3BaseUrl = configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:5100";
        var feederBaseUrl = configuration.GetValue<string>("VendorAdapter:Feeder:BaseUrl") ?? "http://localhost:5200";

        // RIOT3 adapter HttpClient with Polly resilience
        services.AddHttpClient<Riot3CommandService>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Feeder adapter HttpClient with Polly resilience
        services.AddHttpClient<FeederCommandService>(client =>
        {
            client.BaseAddress = new Uri(feederBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Register all command services (resolved by factory)
        services.AddScoped<IVehicleCommandService, Riot3CommandService>();
        services.AddScoped<IVehicleCommandService, FeederCommandService>();
        services.AddScoped<IVehicleCommandService, SimulatorCommandService>();

        // Register the adapter factory
        services.AddScoped<IVendorAdapterFactory, VendorAdapterFactory>();

        // Action catalog (singleton in-memory; swap for DB-backed impl in production)
        services.AddSingleton<IActionCatalogService, InMemoryActionCatalogService>();

        return services;
    }
}
