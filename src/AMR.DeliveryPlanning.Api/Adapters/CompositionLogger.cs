using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;
using Microsoft.Extensions.Configuration;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Logs the composition-root swaps and Phase D worker-mode flag at startup
// so operators / load-testers can confirm container role + vendor isolation
// without having to trigger an order first. Fired once on boot via the
// IHostedService lifecycle — no recurring overhead.
//
// Output example (API container, vendor enabled, outbox disabled):
//   [Composition] IRobotOrderDispatcher = Riot3OrderDispatcherAdapter
//                 | IRiot3OrderQueryService = Riot3OrderQueryService
//                 | Outbox:RunInThisProcess = False
//
// Output example (outbox-worker container, vendor NoOp, outbox enabled):
//   [Composition] IRobotOrderDispatcher = NoOpOrderDispatcherAdapter
//                 | IRiot3OrderQueryService = NoOpRiot3OrderQueryService
//                 | Outbox:RunInThisProcess = True
internal sealed class CompositionLogger : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompositionLogger> _logger;

    public CompositionLogger(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<CompositionLogger> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRobotOrderDispatcher>();
        var queryService = scope.ServiceProvider.GetRequiredService<IRiot3OrderQueryService>();
        var runOutboxHere = _configuration.GetValue<bool>("Outbox:RunInThisProcess", true);

        _logger.LogInformation(
            "[Composition] IRobotOrderDispatcher = {Dispatcher} | IRiot3OrderQueryService = {QueryService} | Outbox:RunInThisProcess = {OutboxHere}",
            dispatcher.GetType().Name,
            queryService.GetType().Name,
            runOutboxHere);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
