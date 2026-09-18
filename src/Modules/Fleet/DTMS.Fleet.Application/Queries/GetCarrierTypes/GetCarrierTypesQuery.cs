using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarrierTypes;

/// <summary>
/// <c>CoverAttachmentId</c> is an id, not a URL, for the same reason as on
/// CarrierListDto: the client turns it into a stable address the browser can
/// cache, which a signed URL — new on every call, expiring within the hour —
/// could never be.
///
/// <para><c>CarrierCount</c> is the number of carriers holding this type. It is
/// on the read model rather than left for the UI to derive because it decides
/// whether the type can be deleted at all: without it the only way to learn a
/// type is in use is to try to delete it and read the 409.</para>
/// </summary>
public record CarrierTypeDto(
    Guid Id,
    string Code,
    string DisplayName,
    string AmrCapability,
    double? MaxWeightKg,
    int? MaxSlots,
    string? Description,
    Guid? CoverAttachmentId,
    int PhotoCount,
    int CarrierCount);

public record GetCarrierTypesQuery : IQuery<List<CarrierTypeDto>>;

public class GetCarrierTypesQueryHandler : IQueryHandler<GetCarrierTypesQuery, List<CarrierTypeDto>>
{
    private readonly ICarrierTypeRepository _repository;
    private readonly IAttachmentRepository _attachments;
    private readonly ICarrierRepository _carriers;

    public GetCarrierTypesQueryHandler(
        ICarrierTypeRepository repository,
        IAttachmentRepository attachments,
        ICarrierRepository carriers)
    {
        _repository = repository;
        _attachments = attachments;
        _carriers = carriers;
    }

    public async Task<Result<List<CarrierTypeDto>>> Handle(
        GetCarrierTypesQuery request, CancellationToken cancellationToken)
    {
        var carrierTypes = await _repository.GetAllAsync(cancellationToken);
        var ids = carrierTypes.Select(c => c.Id).ToArray();

        // One query for every type, never one per row — twice over.
        var photos = await _attachments.GetSummariesForOwnersAsync(
            AttachmentOwner.CarrierType, ids, cancellationToken);
        var carrierCounts = await _carriers.CountsByCarrierTypeAsync(ids, cancellationToken);

        return Result<List<CarrierTypeDto>>.Success(carrierTypes.Select(c =>
        {
            var hasPhotos = photos.TryGetValue(c.Id, out var photo);
            return new CarrierTypeDto(
                c.Id, c.Code, c.DisplayName, c.AmrCapability,
                c.MaxWeightKg, c.MaxSlots, c.Description,
                hasPhotos ? photo.CoverId : null,
                hasPhotos ? photo.Count : 0,
                carrierCounts.TryGetValue(c.Id, out var carriers) ? carriers : 0);
        }).ToList());
    }
}
