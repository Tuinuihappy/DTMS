using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.CreateMap;

internal sealed class CreateMapCommandHandler : ICommandHandler<CreateMapCommand, Guid>
{
    private readonly IMapRepository _mapRepository;

    public CreateMapCommandHandler(IMapRepository mapRepository)
    {
        _mapRepository = mapRepository;
    }

    public async Task<Result<Guid>> Handle(CreateMapCommand request, CancellationToken cancellationToken)
    {
        var map = new Map(
            Guid.NewGuid(),
            request.Name,
            request.Version,
            request.Width,
            request.Height,
            request.MapData);

        if (!string.IsNullOrWhiteSpace(request.VendorRef))
        {
            map.SetVendorRef(request.VendorRef.Trim());
        }

        await _mapRepository.AddAsync(map, cancellationToken);
        await _mapRepository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(map.Id);
    }
}
