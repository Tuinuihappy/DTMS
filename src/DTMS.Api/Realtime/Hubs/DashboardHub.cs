using DTMS.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Hubs;

/// <summary>
/// Dashboard board-key subscriptions. Each dashboard page subscribes to
/// only the board(s) it renders (orders / fleet / funnel / sla) — keeps
/// the wire scoped to what the operator is actually looking at.
///
/// Counters are pushed via <see cref="IDashboardClient.CountersUpdated"/>
/// in 250 ms batches (see P0.B11 <c>DashboardCounterBatcher</c>) so the
/// chart re-render rate is bounded even when 100+ status transitions per
/// second occur during stress windows.
/// </summary>
[Authorize]
public sealed class DashboardHub : Hub<IDashboardClient>
{
    public Task Subscribe(string boardKey)
    {
        if (string.IsNullOrWhiteSpace(boardKey))
            throw new HubException("boardKey is required.");
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupKey(boardKey));
    }

    public Task Unsubscribe(string boardKey)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupKey(boardKey));

    public static string GroupKey(string boardKey) => $"dashboard:{boardKey}";
}
