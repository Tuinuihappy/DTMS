using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Logs the composition-root swaps controlled by VendorAdapter:Riot3:Enabled
// at startup so operators / load-testers can confirm vendor-isolation
// without having to trigger an order first. Fired once on boot via the
// IHostedService lifecycle — no recurring overhead.
//
// Output example:
//   [Composition] IRobotOrderDispatcher    = Riot3OrderDispatcherAdapter
//   [Composition] IRiot3OrderQueryService  = Riot3OrderQueryService
// or with Vendor:Riot3:Enabled=false:
//   [Composition] IRobotOrderDispatcher    = NoOpOrderDispatcherAdapter
//   [Composition] IRiot3OrderQueryService  = NoOpRiot3OrderQueryService
internal sealed class CompositionLogger : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CompositionLogger> _logger;

    public CompositionLogger(IServiceProvider services, ILogger<CompositionLogger> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRobotOrderDispatcher>();
        var queryService = scope.ServiceProvider.GetRequiredService<IRiot3OrderQueryService>();

        _logger.LogInformation(
            "[Composition] IRobotOrderDispatcher = {Dispatcher} | IRiot3OrderQueryService = {QueryService}",
            dispatcher.GetType().Name,
            queryService.GetType().Name);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
