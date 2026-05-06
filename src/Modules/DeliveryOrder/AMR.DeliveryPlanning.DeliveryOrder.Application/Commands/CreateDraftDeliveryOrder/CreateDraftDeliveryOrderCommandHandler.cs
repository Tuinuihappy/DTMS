using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;

public class CreateDraftDeliveryOrderCommandHandler : ICommandHandler<CreateDraftDeliveryOrderCommand, Guid>
{
    private readonly IDeliveryOrderRepository _repository;
    private readonly ILoadUnitProfileRepository _loadUnitProfileRepository;
    private readonly ITenantContext _tenantContext;

    public CreateDraftDeliveryOrderCommandHandler(
        IDeliveryOrderRepository repository,
        ILoadUnitProfileRepository loadUnitProfileRepository,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _loadUnitProfileRepository = loadUnitProfileRepository;
        _tenantContext = tenantContext;
    }

    public async Task<Result<Guid>> Handle(CreateDraftDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        // Pre-load all distinct LoadUnitProfiles to resolve CarrierTypeCode
        var profileCodes = request.OrderItems.Select(p => p.LoadUnitProfileCode).Distinct().ToList();
        var carrierTypeLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in profileCodes)
        {
            var profile = await _loadUnitProfileRepository.GetByCodeAsync(code, cancellationToken);
            if (profile is null)
                return Result<Guid>.Failure($"LoadUnitProfile '{code}' not found.");
            carrierTypeLookup[code] = profile.CarrierTypeCode;
        }

        var serviceWindow = new ServiceWindow(
            request.ServiceWindow?.Earliest,
            request.ServiceWindow?.Latest);

        var order = Domain.Entities.DeliveryOrder.Create(
            _tenantContext.TenantId,
            request.OrderName,
            request.SlaTier,
            serviceWindow,
            request.StructureType,
            request.Tags);

        foreach (var pkg in request.OrderItems)
        {
            var carrierTypeCode = carrierTypeLookup[pkg.LoadUnitProfileCode];
            var contents = pkg.Contents?
                .Select(c => (c.ItemNumber, c.Quantity));

            order.AddPackage(
                pkg.PickupLocationCode, pkg.DropLocationCode,
                carrierTypeCode,
                pkg.Barcode,
                pkg.LoadUnitProfileCode,
                pkg.GrossWeightKg,
                contents);
        }

        if (request.Schedule != null)
            order.SetRecurringSchedule(
                request.Schedule.CronExpression,
                request.Schedule.ValidFrom,
                request.Schedule.ValidUntil);

        await _repository.AddAsync(order, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}
