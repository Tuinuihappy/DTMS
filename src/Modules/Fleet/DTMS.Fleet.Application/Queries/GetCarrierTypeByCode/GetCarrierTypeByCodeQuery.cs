using DTMS.Fleet.Application.Queries.GetCarrierTypes;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarrierTypeByCode;

/// <summary>Single carrier type by its code. Reuses <see cref="CarrierTypeDto"/>
/// — the catalogue has no fields the list leaves out, so a second near-identical
/// DTO would only be two shapes to keep in sync.</summary>
public record GetCarrierTypeByCodeQuery(string Code) : IQuery<CarrierTypeDto>;

public class GetCarrierTypeByCodeQueryHandler : IQueryHandler<GetCarrierTypeByCodeQuery, CarrierTypeDto>
{
    private readonly ICarrierTypeRepository _carrierTypes;
    private readonly IAttachmentRepository _attachments;
    private readonly ICarrierRepository _carriers;

    public GetCarrierTypeByCodeQueryHandler(
        ICarrierTypeRepository carrierTypes,
        IAttachmentRepository attachments,
        ICarrierRepository carriers)
    {
        _carrierTypes = carrierTypes;
        _attachments = attachments;
        _carriers = carriers;
    }

    public async Task<Result<CarrierTypeDto>> Handle(
        GetCarrierTypeByCodeQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Result<CarrierTypeDto>.Failure("Code is required.");

        var carrierType = await _carrierTypes.GetByCodeAsync(request.Code, cancellationToken);
        if (carrierType is null)
            return Result<CarrierTypeDto>.Failure(
                $"CarrierType '{request.Code.Trim().ToUpperInvariant()}' not found.");

        // Filled in rather than left null/0, for the same reason as
        // GetCarrierByCode: the list and this endpoint share one DTO, and a
        // detail claiming "no photos" while the row shows three would be a lie
        // any future caller would believe.
        var photos = await _attachments.GetSummariesForOwnersAsync(
            AttachmentOwner.CarrierType, [carrierType.Id], cancellationToken);
        var hasPhotos = photos.TryGetValue(carrierType.Id, out var photo);

        // Singular count here, batched in the list handler — one type asking
        // about itself is not the query a page of them needs.
        var carrierCount = await _carriers.CountForCarrierTypeAsync(carrierType.Id, cancellationToken);

        return Result<CarrierTypeDto>.Success(new CarrierTypeDto(
            carrierType.Id,
            carrierType.Code,
            carrierType.DisplayName,
            carrierType.AmrCapability,
            carrierType.MaxWeightKg,
            carrierType.MaxSlots,
            carrierType.Description,
            hasPhotos ? photo.CoverId : null,
            hasPhotos ? photo.Count : 0,
            carrierCount));
    }
}
