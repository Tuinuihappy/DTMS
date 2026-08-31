using DTMS.Fleet.Application.Services;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.RegisterCarrier;

/// <summary>
/// Callers speak in carrier-type <b>codes</b> — the id is an implementation
/// detail of the FK (ADR-019). <paramref name="CommissionedAt"/> is the date the
/// carrier physically entered service and may be backdated; it is not the row's
/// creation time, which the aggregate stamps itself.
/// </summary>
public record RegisterCarrierCommand(
    string CarrierCode,
    string CarrierTypeCode,
    string? Barcode = null,
    string? DisplayName = null,
    string? CurrentLocationCode = null,
    DateTime? CommissionedAt = null) : ICommand<Guid>;

internal sealed class RegisterCarrierCommandHandler : ICommandHandler<RegisterCarrierCommand, Guid>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierTypeRepository _carrierTypes;
    private readonly ICurrentActorContext _actor;

    public RegisterCarrierCommandHandler(
        ICarrierRepository carriers,
        ICarrierTypeRepository carrierTypes,
        ICurrentActorContext actor)
    {
        _carriers = carriers;
        _carrierTypes = carrierTypes;
        _actor = actor;
    }

    public async Task<Result<Guid>> Handle(RegisterCarrierCommand request, CancellationToken cancellationToken)
    {
        // Validation is inline rather than a FluentValidation validator: the Fleet
        // assembly is not registered with AddValidatorsFromAssembly, so a validator
        // placed here would never run — it would fail open, silently.
        if (string.IsNullOrWhiteSpace(request.CarrierTypeCode))
            return Result<Guid>.Failure("CarrierTypeCode is required.");

        string code;
        try
        {
            code = Carrier.NormalizeAndValidateCode(request.CarrierCode);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        var carrierType = await _carrierTypes.GetByCodeAsync(request.CarrierTypeCode, cancellationToken);
        if (carrierType is null)
            return Result<Guid>.Failure($"CarrierType '{request.CarrierTypeCode.Trim().ToUpperInvariant()}' not found.");

        // Pre-checks exist to name the offending field. They cannot close the
        // window before the INSERT — the unique indexes do that, and
        // ExceptionHandlingMiddleware turns a lost race into a 409 rather than
        // a scrubbed 500.
        if (await _carriers.CodeExistsAsync(code, cancellationToken))
            return Result<Guid>.Failure($"Carrier '{code}' already exists.");

        if (!string.IsNullOrWhiteSpace(request.Barcode)
            && await _carriers.BarcodeExistsAsync(request.Barcode, null, cancellationToken))
            return Result<Guid>.Failure($"Barcode '{request.Barcode.Trim()}' is already assigned to another carrier.");

        var carrier = new Carrier(
            code,
            carrierType.Id,
            request.Barcode,
            request.DisplayName,
            request.CurrentLocationCode,
            request.CommissionedAt,
            ActorName.Of(_actor));

        await _carriers.AddAsync(carrier, cancellationToken);
        await _carriers.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(carrier.Id);
    }
}
