using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Dispatch.Application.Commands.SourceSystemTrip;

/// <summary>
/// Runs the origin guard for every source-system trip handler, then hands the
/// subclass a <see cref="Trip"/> the caller is proven to own.
///
/// <para><b>Why a base class and not a copied block.</b> Each of these handlers
/// used to open with the same four lines calling
/// <see cref="SourceTripOriginAuthorizer"/>. That made the guard something a
/// new handler could forget, or run after logic that already leaked state —
/// and the state these handlers can leak is whether a trip id exists and what
/// status it is in, for trips belonging to somebody else's system. Here
/// <see cref="Handle"/> is not virtual, so a subclass gets no chance to run
/// before the guard: it can only fill in <see cref="HandleAuthorized"/>, which
/// receives an already-authorized trip.</para>
/// </summary>
public abstract class SourceTripCommandHandlerBase<TCommand> : ICommandHandler<TCommand>
    where TCommand : ISourceTripCommand, ICommand
{
    private readonly ITripRepository _trips;
    private readonly IDeliveryOrderStatusReader _orders;

    protected SourceTripCommandHandlerBase(ITripRepository trips, IDeliveryOrderStatusReader orders)
    {
        _trips = trips;
        _orders = orders;
    }

    /// <summary>Repository for subclasses that persist their mutation.</summary>
    protected ITripRepository Trips => _trips;

    /// <summary>
    /// The parent order's read side. The guard already used it to resolve
    /// ownership; the drop handler also reads the POD policy from it.
    /// </summary>
    protected IDeliveryOrderStatusReader Orders => _orders;

    public async Task<Result> Handle(TCommand request, CancellationToken cancellationToken)
    {
        var resolved = await SourceTripOriginAuthorizer.ResolveAsync(
            _trips, _orders, request.TripId, request.SourceSystemKey, cancellationToken);
        if (!resolved.IsSuccess) return Result.Failure(resolved.Error);
        return await HandleAuthorized(request, resolved.Value, cancellationToken);
    }

    /// <param name="trip">
    /// Loaded and confirmed to belong to <c>request.SourceSystemKey</c>. Safe to
    /// act on, and safe to describe back to the caller in an error message.
    /// </param>
    protected abstract Task<Result> HandleAuthorized(
        TCommand request, Trip trip, CancellationToken cancellationToken);
}
