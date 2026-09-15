using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarrierTypes;

/// <summary>
/// <c>CoverAttachmentId</c> is an id, not a URL, for the same reason as on
/// CarrierListDto: the client turns it into a stable address the browser can
/// cache, which a signed URL — new on every call, expiring within the hour —
/// could never be.
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
    int PhotoCount);

public record GetCarrierTypesQuery : IQuery<List<CarrierTypeDto>>;

public class GetCarrierTypesQueryHandler : IQueryHandler<GetCarrierTypesQuery, List<CarrierTypeDto>>
{
    private readonly ICarrierTypeRepository _repository;
    private readonly IAttachmentRepository _attachments;

    public GetCarrierTypesQueryHandler(ICarrierTypeRepository repository, IAttachmentRepository attachments)
    {
        _repository = repository;
        _attachments = attachments;
    }

    public async Task<Result<List<CarrierTypeDto>>> Handle(
        GetCarrierTypesQuery request, CancellationToken cancellationToken)
    {
        var carrierTypes = await _repository.GetAllAsync(cancellationToken);

        // One query for every type, never one per row.
        var photos = await _attachments.GetSummariesForOwnersAsync(
            AttachmentOwner.CarrierType, carrierTypes.Select(c => c.Id).ToArray(), cancellationToken);

        return Result<List<CarrierTypeDto>>.Success(carrierTypes.Select(c =>
        {
            var hasPhotos = photos.TryGetValue(c.Id, out var photo);
            return new CarrierTypeDto(
                c.Id, c.Code, c.DisplayName, c.AmrCapability,
                c.MaxWeightKg, c.MaxSlots, c.Description,
                hasPhotos ? photo.CoverId : null,
                hasPhotos ? photo.Count : 0);
        }).ToList());
    }
}
