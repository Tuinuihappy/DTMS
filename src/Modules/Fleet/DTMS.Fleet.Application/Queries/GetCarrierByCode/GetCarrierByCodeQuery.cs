using DTMS.Fleet.Application.Queries.GetCarriers;
using DTMS.Fleet.Domain.Entities;
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
    private readonly IAttachmentRepository _attachments;

    public GetCarrierByCodeQueryHandler(
        ICarrierRepository carriers,
        ICarrierTypeRepository carrierTypes,
        IAttachmentRepository attachments)
    {
        _carriers = carriers;
        _carrierTypes = carrierTypes;
        _attachments = attachments;
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

        // Filled in rather than left null/0: the list and this endpoint share one
        // DTO, and a detail that said "no photos" while the row showed three would
        // be a lie any future caller would believe.
        var photos = await _attachments.GetSummariesForOwnersAsync(
            AttachmentOwner.Carrier, [carrier.Id], cancellationToken);
        var hasPhotos = photos.TryGetValue(carrier.Id, out var photo);

        return Result<CarrierListDto>.Success(new CarrierListDto(
            carrier.Id,
            carrier.CarrierCode,
            carrierType?.Code ?? string.Empty,
            carrier.DisplayName,
            carrier.Status.ToString(),
            carrier.MaintenanceReason,
            carrier.MaintenanceSince,
            carrier.CurrentLocationCode,
            carrier.LastSeenAt,
            carrier.CommissionedAt,
            carrier.RetiredAt,
            carrier.RetireReason,
            hasPhotos ? photo.CoverId : null,
            hasPhotos ? photo.Count : 0));
    }
}
