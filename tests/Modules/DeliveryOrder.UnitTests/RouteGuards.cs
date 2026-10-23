using DTMS.DeliveryOrder.Application.Services;
using DTMS.DeliveryOrder.Domain.Entities;
using DTMS.DeliveryOrder.Domain.Enums;
using NSubstitute;

namespace DeliveryOrder.UnitTests;

/// <summary>Route-guard doubles for handler tests that aren't about routes.</summary>
internal static class RouteGuards
{
    /// <summary>Every route is dispatchable. (An unstubbed substitute would
    /// answer CheckAsync with a substitute rejection, not null.)</summary>
    public static IRouteDispatchGuard Passing()
    {
        var guard = Substitute.For<IRouteDispatchGuard>();
        guard.CheckAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<Item>>(), Arg.Any<TransportMode>(),
                Arg.Any<IReadOnlyDictionary<string, Guid>?>(), Arg.Any<CancellationToken>())
            .Returns((RouteRejection?)null);
        return guard;
    }
}
