using AMR.DeliveryPlanning.Api.Realtime.Hubs.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AMR.DeliveryPlanning.Api.Realtime.Hubs;

/// <summary>
/// Phase 4.6 — Dispatcher /dispatch/manual page subscribes here for
/// realtime hints when an override is decided or a Manual trip is
/// reassigned. Single broadcast group ("manual-board") because the
/// page is operator-board-wide — no per-operator scoping needed.
///
/// Auth via the existing [Authorize] policy (default JWT scheme);
/// admins/dispatchers in the JWT 'role' claim are who use this page.
/// </summary>
[Authorize]
public sealed class ManualBoardHub : Hub<IManualBoardClient>
{
    public const string BoardGroup = "manual-board";

    public Task Subscribe() => Groups.AddToGroupAsync(Context.ConnectionId, BoardGroup);
    public Task Unsubscribe() => Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardGroup);
}
