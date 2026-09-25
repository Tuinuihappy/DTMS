namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// Marker for every <c>/api/v1/source/trips/*</c> command, exposing the two
/// fields the origin guard needs: which trip, and which source system claims
/// to own it. Both are already positional parameters on each command record —
/// <c>SourceSystemKey</c> is pinned from the authenticated principal by the
/// endpoint, never read from the request body.
/// </summary>
/// <seealso cref="SourceTripCommandHandlerBase{TCommand}"/>
public interface ISourceTripCommand
{
    Guid TripId { get; }
    string SourceSystemKey { get; }
}
