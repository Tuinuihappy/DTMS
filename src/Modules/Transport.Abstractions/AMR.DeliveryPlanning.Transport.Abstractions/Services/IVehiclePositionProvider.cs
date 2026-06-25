using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

namespace AMR.DeliveryPlanning.Transport.Abstractions.Services;

/// <summary>
/// Streams vehicle / operator position updates from a per-mode source.
/// Each mode plugs in its own implementation — RIOT3 polling for AMR,
/// mobile-app heartbeat for Manual, 3PL GPS for Fleet — and a single
/// orchestrator background service consumes all registered providers
/// into the shared position store (which feeds the SignalR map layer).
///
/// Phase 1: only declares the interface. Existing
/// <c>Riot3PositionPollerService</c> stays as-is. Phase 3 will refactor
/// it into <c>Riot3PositionProvider</c> implementing this interface and
/// introduce the orchestrator that multiplexes providers. Defining the
/// interface now lets Phase 4 / 5 add their providers without later
/// refactor pressure on the contract.
/// </summary>
public interface IVehiclePositionProvider
{
    TransportMode Mode { get; }

    /// <summary>
    /// Long-running stream of position updates. Cancellation token is
    /// honoured for graceful shutdown. Implementations are responsible
    /// for retry / backoff if their source is transiently unavailable.
    /// </summary>
    IAsyncEnumerable<PositionUpdate> StreamAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Single position observation. <see cref="X"/> / <see cref="Y"/> are
/// factory-local for AMR (RIOT3 map coordinate frame) and lat/lng for
/// Manual / Fleet — consumers know which frame by checking
/// <see cref="Mode"/>. Optional <see cref="BatteryLevel"/> is AMR-only;
/// Manual / Fleet leave null.
/// </summary>
public sealed record PositionUpdate(
    TransportMode Mode,
    Guid VehicleId,
    double X,
    double Y,
    double? Theta,
    DateTime ObservedAt,
    double? BatteryLevel = null);
