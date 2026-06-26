using AMR.DeliveryPlanning.Transport.Abstractions.Services;
using AMR.DeliveryPlanning.Transport.Amr.Options;
using AMR.DeliveryPlanning.Transport.Amr.Webhooks;
using AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Data;
using AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Extensions;
using AMR.DeliveryPlanning.Transport.Amr.Infrastructure.Services;
using AMR.DeliveryPlanning.Transport.Amr.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMR.DeliveryPlanning.Transport.Amr.Infrastructure;

public static class VendorAdapterServiceRegistration
{
    public static IServiceCollection AddVendorAdapterInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Resilient connection: EnableRetryOnFailure absorbs transient Npgsql
        // errors at the EF Core layer. Mirrors ConfigureNpgsql in
        // ModuleServiceRegistration — kept inline because pulling a helper
        // out of the API project would create a cross-layer dependency.
        //
        // Phase B Step B1 — resolve the shared NpgsqlDataSource singleton
        // (registered in ModuleServiceRegistration.AddAllModules) via the
        // (sp, o) factory so this DbContext joins the same pool as the
        // other 8. Before: separate UseNpgsql(connectionString) created an
        // independent Npgsql static pool keyed by connection string,
        // doubling the connection footprint under load.
        services.AddDbContext<VendorAdapterDbContext>((sp, o) => o.UseNpgsql(
            sp.GetRequiredService<Npgsql.NpgsqlDataSource>(),
            npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null)));

        var riot3BaseUrl = configuration.GetValue<string>("VendorAdapter:Riot3:BaseUrl") ?? "http://localhost:5100";
        var riot3ApiKey = configuration.GetValue<string>("VendorAdapter:Riot3:ApiKey");

        // RIOT3 adapter HttpClient with Polly resilience
        services.AddHttpClient<Riot3CommandService>(client =>
        {
            client.BaseAddress = new Uri(riot3BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
        })
        .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
        .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());

        // Read-side query client for the reconciliation poller (GET /orders/{key}?isUpper=true).
        // Vendor seam: same VendorAdapter:Riot3:Enabled flag that controls
        // IRobotOrderDispatcher also gates the reconciler's outbound GET path.
        // When false, NoOpRiot3OrderQueryService returns null for every query
        // and the reconciler treats those as "RIOT3 has no record yet" and
        // skips for the tick — zero outbound HTTP traffic, no side effects.
        // Default true (production safety, opt-out explicitly for dev/test).
        var riot3Enabled = configuration.GetValue<bool>("VendorAdapter:Riot3:Enabled", true);
        if (riot3Enabled)
        {
            services.AddHttpClient<IRiot3OrderQueryService, Riot3OrderQueryService>(client =>
            {
                client.BaseAddress = new Uri(riot3BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
                if (!string.IsNullOrWhiteSpace(riot3ApiKey))
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", riot3ApiKey);
            })
            .AddPolicyHandler(ResilienceExtensions.GetRetryPolicy())
            .AddPolicyHandler(ResilienceExtensions.GetCircuitBreakerPolicy());
        }
        else
        {
            services.AddSingleton<IRiot3OrderQueryService, NoOpRiot3OrderQueryService>();
        }

        // Reconciliation poller — safety net for missed envelope webhooks.
        // Gated by Dispatch:Reconciliation:Enabled; off by default.
        services.Configure<ReconciliationOptions>(
            configuration.GetSection(ReconciliationOptions.SectionName));

        // Track C (Phase D follow-up) — only the api container polls RIOT3.
        // The outbox-worker sibling skips this registration entirely via
        // Workers__VendorPollers__RunInThisProcess=false so we don't double
        // the reconciliation HTTP load on the vendor.
        var runVendorPollers = configuration
            .GetValue("Workers:VendorPollers:RunInThisProcess", true);
        if (runVendorPollers)
        {
            services.AddHostedService<Riot3ReconciliationService>();
        }

        // Inbound webhook auth — IP allowlist + URL-path secret. Reads
        // VendorAdapter:Riot3:Webhook section. RequireAuth=false by
        // default so warn-mode logs the actual remote IP the container
        // sees before ops enforce.
        services.Configure<Riot3WebhookOptions>(
            configuration.GetSection(Riot3WebhookOptions.SectionName));
        services.AddScoped<Riot3WebhookAuthFilter>();

        services.AddScoped<IVehicleIdentityResolver, VehicleIdentityResolver>();
        services.AddScoped<IVendorAdapterOutbox, VendorAdapterOutbox>();

        // Action catalog — DB-backed, seeded with defaults on first use
        services.AddScoped<IActionCatalogService, DbActionCatalogService>();

        return services;
    }
}
