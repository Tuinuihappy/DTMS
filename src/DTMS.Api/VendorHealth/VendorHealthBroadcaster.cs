using AMR.DeliveryPlanning.Api.Realtime.Hubs;
using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.VendorHealth;

/// <summary>
/// Subscribes to <see cref="IVendorHealthStore.StatusChanged"/> and pushes
/// the new snapshot into the <c>dashboard:vendor-health</c> group via
/// <see cref="DashboardHub"/>. The state machine already debounces flap,
/// so every event published here represents a real transition the UI
/// should render.
/// </summary>
public sealed class VendorHealthBroadcaster : IHostedService
{
    public const string GroupBoardKey = "vendor-health";

    private readonly IVendorHealthStore _store;
    private readonly IHubContext<DashboardHub, IDashboardClient> _hubContext;
    private readonly ILogger<VendorHealthBroadcaster> _logger;

    public VendorHealthBroadcaster(
        IVendorHealthStore store,
        IHubContext<DashboardHub, IDashboardClient> hubContext,
        ILogger<VendorHealthBroadcaster> logger)
    {
        _store = store;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _store.StatusChanged += OnStatusChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _store.StatusChanged -= OnStatusChanged;
        return Task.CompletedTask;
    }

    private void OnStatusChanged(object? sender, VendorHealthSnapshot snapshot)
    {
        _ = BroadcastAsync(snapshot);
    }

    private async Task BroadcastAsync(VendorHealthSnapshot snapshot)
    {
        try
        {
            var dto = VendorHealthDto.From(snapshot);
            await _hubContext.Clients
                .Group(DashboardHub.GroupKey(GroupBoardKey))
                .VendorHealthChanged(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to broadcast vendor health change for {Vendor} (status={Status})",
                snapshot.Vendor, snapshot.Status);
        }
    }
}
