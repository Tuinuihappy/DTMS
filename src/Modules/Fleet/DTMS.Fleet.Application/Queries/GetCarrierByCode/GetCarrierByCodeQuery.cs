using DTMS.Fleet.Application.Queries.GetCarriers;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarrierByCode;

/// <summary>Single carrier by its code. Reuses <see cref="CarrierListDto"/> —
/// the detail view shows exactly the registry fields, so a second near-identical
/// DTO would only be two shapes to keep in sync.</summary>
public record GetCarrierByCodeQuery(string CarrierCode) : IQuery<CarrierListDto>;

public class GetCarrierByCodeQueryHandler : IQueryHandler<GetCarrierByCodeQuery, CarrierListDto>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierTypeRepository _carrierTypes;

    public GetCarrierByCodeQueryHandler(ICarrierRepository carriers, ICarrierTypeRepository carrierTypes)
    {
        _carriers = carriers;
        _carrierTypes = carrierTypes;
    }

    public async Task<Result<CarrierListDto>> Handle(
        GetCarrierByCodeQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CarrierCode))
            return Result<CarrierListDto>.Failure("CarrierCode is required.");

        var carrier = await _carriers.GetByCodeAsync(request.CarrierCode, cancellationToken);
        if (carrier is null)
            return Result<CarrierListDto>.Failure(
                $"Carrier '{request.CarrierCode.Trim().ToUpperInvariant()}' not found.");

        var carrierType = await _carrierTypes.GetByIdAsync(carrier.CarrierTypeId, cancellationToken);

        return Result<CarrierListDto>.Success(new CarrierListDto(
            carrier.Id,
            carrier.CarrierCode,
            carrierType?.Code ?? string.Empty,
            carrier.Barcode,
            carrier.DisplayName,
            carrier.Status.ToString(),
            carrier.MaintenanceReason,
            carrier.MaintenanceSince,
            carrier.CurrentLocationCode,
            carrier.LastSeenAt,
            carrier.CommissionedAt,
            carrier.RetiredAt,
            carrier.RetireReason));
    }
}
