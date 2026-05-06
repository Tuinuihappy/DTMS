using AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.CreateDraftDeliveryOrder;
using AMR.DeliveryPlanning.DeliveryOrder.Application.Services;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.ValueObjects;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.SharedKernel.Tenancy;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.BulkSubmitDeliveryOrders;

public class BulkSubmitDeliveryOrdersCommandHandler : ICommandHandler<BulkSubmitDeliveryOrdersCommand, List<Guid>>
{
    private readonly IDeliveryOrderRepository _repo;
    private readonly ILoadUnitProfileRepository _loadUnitProfileRepository;
    private readonly StationValidationService _stationValidation;
    private readonly ITenantContext _tenantContext;

    public BulkSubmitDeliveryOrdersCommandHandler(
        IDeliveryOrderRepository repo,
        ILoadUnitProfileRepository loadUnitProfileRepository,
        StationValidationService stationValidation,
        ITenantContext tenantContext)
    {
        _repo = repo;
        _loadUnitProfileRepository = loadUnitProfileRepository;
        _stationValidation = stationValidation;
        _tenantContext = tenantContext;
    }

    public async Task<Result<List<Guid>>> Handle(BulkSubmitDeliveryOrdersCommand request, CancellationToken cancellationToken)
    {
        if (request.Orders.Count == 0)
            return Result<List<Guid>>.Failure("No orders provided.");

        // Pre-load all distinct profiles across all orders
        var allProfileCodes = request.Orders
            .SelectMany(o => o.OrderItems.Select(p => p.LoadUnitProfileCode))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var carrierTypeLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in allProfileCodes)
        {
            var profile = await _loadUnitProfileRepository.GetByCodeAsync(code, cancellationToken);
            if (profile is null)
                return Result<List<Guid>>.Failure($"LoadUnitProfile '{code}' not found.");
            carrierTypeLookup[code] = profile.CarrierTypeCode;
        }

        var orders = new List<Domain.Entities.DeliveryOrder>();

        foreach (var cmd in request.Orders)
        {
            var serviceWindow = new ServiceWindow(
                cmd.ServiceWindow?.Earliest,
                cmd.ServiceWindow?.Latest);

            var order = Domain.Entities.DeliveryOrder.Create(
                _tenantContext.TenantId, cmd.OrderName,
                cmd.SlaTier, serviceWindow, cmd.StructureType, cmd.Tags);

            foreach (var pkg in cmd.OrderItems)
            {
                var carrierTypeCode = carrierTypeLookup[pkg.LoadUnitProfileCode];
                var contents = pkg.Contents?.Select(c => (c.ItemNumber, c.Quantity));

                order.AddPackage(
                    pkg.PickupLocationCode, pkg.DropLocationCode,
                    carrierTypeCode,
                    pkg.Barcode,
                    pkg.LoadUnitProfileCode,
                    pkg.GrossWeightKg,
                    contents);
            }

            if (cmd.Schedule != null)
                order.SetRecurringSchedule(cmd.Schedule.CronExpression, cmd.Schedule.ValidFrom, cmd.Schedule.ValidUntil);

            var stationMap = await _stationValidation.BuildStationMapAsync(order.Legs, cancellationToken);
            if (stationMap.IsFailure) return Result<List<Guid>>.Failure(stationMap.Error);

            order.Submit();
            order.MarkAsValidated(stationMap.Value);
            order.MarkReadyToPlan();

            orders.Add(order);
        }

        await _repo.AddRangeAsync(orders, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        return Result<List<Guid>>.Success(orders.Select(o => o.Id).ToList());
    }
}
