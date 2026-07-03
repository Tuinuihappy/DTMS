using DTMS.Api.Auth;
using DTMS.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DTMS.Api.Realtime.Hubs;

/// <summary>
/// WMS PR-4b (PR-D) — operator pool broadcast hub. All connected operator
/// PWAs join a single group <c>"operator-pool"</c> and receive every pool
/// event (added/claimed/removed). No zone or warehouse filter — the pool
/// is universally visible per the P4b design decision.
///
/// Auth: reuses the <see cref="OperatorAuthPolicies.OperatorOnly"/> policy
/// (Operator | Supervisor | Admin role via the default JwtBearer scheme).
/// The client sends the bearer via the SignalR standard
/// <c>?access_token=</c> query param when using WebSocket transport
/// (see JwtBearer OnMessageReceived configuration in Program.cs).
///
/// The group is auto-joined on connect via <see cref="OnConnectedAsync"/>
/// so clients don't have to remember to invoke <c>Subscribe</c> after each
/// reconnect — the connection alone is intent enough.
/// </summary>
[Authorize(Policy = OperatorAuthPolicies.OperatorOnly)]
public sealed class OperatorPoolHub : Hub<IOperatorPoolClient>
{
    public const string PoolGroup = "operator-pool";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, PoolGroup);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PoolGroup);
        await base.OnDisconnectedAsync(exception);
    }

    // Explicit no-op methods so the frontend's shared useHubSubscription
    // hook (which invokes Subscribe on mount + on reconnect) has something
    // to call. The actual group membership is managed via OnConnectedAsync;
    // this just re-affirms it and lets the client set its `connected` flag.
    public Task Subscribe() => Groups.AddToGroupAsync(Context.ConnectionId, PoolGroup);
    public Task Unsubscribe() => Groups.RemoveFromGroupAsync(Context.ConnectionId, PoolGroup);
}
