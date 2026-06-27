using DTMS.Planning.Application.Services;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Api.Adapters;

// Dev/load-test seam — short-circuits the IRobotOrderDispatcher contract
// without contacting RIOT3. Picked at composition time when
// VendorAdapter:Riot3:Enabled=false. Returns a recognizable fake order key
// ("NOOP-<guid>") so test artifacts are easy to grep in DB queries
// (WHERE "VendorOrderKey" LIKE 'NOOP-%') and harmless empty request JSON
// so downstream Trip.VendorRequestSnapshot keeps a valid shape.
//
// Logged at INF level on every skip — silent skipping is a worse failure
// mode than the symptom we're avoiding. If you see "NoOp" in prod logs
// something is misconfigured.
internal sealed class NoOpOrderDispatcherAdapter : IRobotOrderDispatcher
{
    private readonly ILogger<NoOpOrderDispatcherAdapter> _logger;

    public NoOpOrderDispatcherAdapter(ILogger<NoOpOrderDispatcherAdapter> logger)
    {
        _logger = logger;
    }

    public Task<Result<RobotOrderDispatchResult>> SendAsync(
        string upperKey,
        ResolvedOrder order,
        CancellationToken cancellationToken = default)
    {
        var fakeOrderKey = $"NOOP-{Guid.NewGuid():N}";
        _logger.LogInformation(
            "[NoOp] Skipping RIOT3 dispatch for upperKey={UpperKey} with {MissionCount} mission(s); returning fake orderKey={FakeOrderKey}",
            upperKey, order.Missions.Count, fakeOrderKey);

        return Task.FromResult(Result<RobotOrderDispatchResult>.Success(
            new RobotOrderDispatchResult(fakeOrderKey, "{}")));
    }
}
