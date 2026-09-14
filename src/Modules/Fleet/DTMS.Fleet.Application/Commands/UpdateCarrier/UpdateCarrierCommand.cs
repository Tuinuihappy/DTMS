using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.UpdateCarrier;

/// <summary>
/// Edits the mutable metadata. <c>CarrierCode</c> is deliberately absent: codes
/// identify one physical carrier forever (ADR-019), and a rename would make
/// historical statements about a code ambiguous just as surely as reuse would.
/// </summary>
public record UpdateCarrierCommand(
    string CarrierCode,
    string CarrierTypeCode,
    string? DisplayName = null,
    DateTime? CommissionedAt = null) : ICommand;

internal sealed class UpdateCarrierCommandHandler : ICommandHandler<UpdateCarrierCommand>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierTypeRepository _carrierTypes;
    private readonly ICurrentActorContext _actor;

    public UpdateCarrierCommandHandler(
        ICarrierRepository carriers,
        ICarrierTypeRepository carrierTypes,
        ICurrentActorContext actor)
    {
        _carriers = carriers;
        _carrierTypes = carrierTypes;
        _actor = actor;
    }

    public async Task<Result> Handle(UpdateCarrierCommand request, CancellationToken cancellationToken)
    {
        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result.Failure($"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        if (string.IsNullOrWhiteSpace(request.CarrierTypeCode))
            return Result.Failure("CarrierTypeCode is required.");

        var carrierType = await _carrierTypes.GetByCodeAsync(request.CarrierTypeCode, cancellationToken);
        if (carrierType is null)
            return Result.Failure($"CarrierType '{request.CarrierTypeCode.Trim().ToUpperInvariant()}' not found.");

        try
        {
            carrier.Update(carrierType.Id, request.DisplayName,
                request.CommissionedAt, ActorName.Of(_actor));
        }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message); }

        await _carriers.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
