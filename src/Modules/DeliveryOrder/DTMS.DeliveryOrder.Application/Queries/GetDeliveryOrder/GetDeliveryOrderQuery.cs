using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.DeliveryOrder.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.DeliveryOrder.Application.Queries.GetDeliveryOrder;

public record DimensionsDto(double LengthMm, double WidthMm, double HeightMm, double VolumeCBM);

public record QuantityDto(double Value, string Uom);

public record ServiceWindowDto(DateTime? EarliestUtc, DateTime? LatestUtc);

public record HazmatDto(string ClassCode, PackingGroup? PackingGroup);

public record TemperatureRangeDto(double? MinC, double? MaxC);

public record PodEventDto(DateTime ScannedAt, string ScannedBy, string Method, string? Reference);

public record ItemDto(
    Guid Id,
    int ItemSeq,
    string ItemId,
    string? Description,
    string PickupLocationCode,
    string DropLocationCode,
    Guid? PickupStationId,
    Guid? DropStationId,
    string? LoadUnitProfileCode,
    DimensionsDto? Dimensions,
    double? WeightKg,
    QuantityDto Quantity,
    HazmatDto? Hazmat,
    TemperatureRangeDto? Temperature,
    IReadOnlyList<HandlingInstruction> HandlingInstructions,
    ItemStatus Status,
    Guid? TripId,
    int? AttemptNumber,
    DateTime? DroppedOffAt,
    PodEventDto? PickupPod,
    PodEventDto? DropPod);

// Phase P5: SourceSystem is now a string (lowercase iam.SystemClients.Key
// slug — e.g. "oms", "manual", "wms-acme"). SourceSystemDisplayName is
// the snapshot captured at create time so admin renames don't retro-
// update historical rows. Both fields go to the wire as-is; FE broadens
// its `SourceSystem` union type to `string`.
public record DeliveryOrderListDto(
    Guid Id,
    string OrderRef,
    string SourceSystem,
    string? SourceSystemDisplayName,
    Priority Priority,
    OrderStatus OrderStatus,
    ServiceWindowDto? ServiceWindow,
    DateTime? SubmittedAt,
    string? CreatedBy,
    string? RequestedBy,
    string? Notes,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems,
    TransportMode? RequestedTransportMode,
    bool? RequiresDropPod,
    bool? RequiresPickupPod);

public record DeliveryOrderDetailDto(
    Guid Id,
    string OrderRef,
    string SourceSystem,
    string? SourceSystemDisplayName,
    Priority Priority,
    OrderStatus OrderStatus,
    ServiceWindowDto? ServiceWindow,
    DateTime? SubmittedAt,
    string? CreatedBy,
    string? RequestedBy,
    string? Notes,
    DateTime CreatedDate,
    DateTime? UpdatedDate,
    double TotalWeightKg,
    double TotalQuantity,
    int TotalItems,
    TransportMode? RequestedTransportMode,
    bool? RequiresDropPod,
    bool? RequiresPickupPod,
    IReadOnlyList<ItemDto> Items);

public record GetDeliveryOrderQuery(Guid OrderId) : IQuery<DeliveryOrderDetailDto>;

public class GetDeliveryOrderQueryHandler : IQueryHandler<GetDeliveryOrderQuery, DeliveryOrderDetailDto>
{
    private readonly IDeliveryOrderRepository _repo;
    public GetDeliveryOrderQueryHandler(IDeliveryOrderRepository repo) => _repo = repo;

    public async Task<Result<DeliveryOrderDetailDto>> Handle(GetDeliveryOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsNoTrackingAsync(request.OrderId, cancellationToken);
        if (order == null) return Result<DeliveryOrderDetailDto>.Failure($"Order {request.OrderId} not found.");

        return Result<DeliveryOrderDetailDto>.Success(DeliveryOrderMapper.MapToDetailDto(order));
    }
}

public record GetDeliveryOrdersQuery(
    OrderStatus? Status,
    StatusBucket? Bucket = null,
    Priority? Priority = null,
    TransportMode? TransportMode = null,
    string? Search = null,
    bool? HasFailedTrip = null,
    bool? HasActiveJob = null,
    string? SortBy = null,
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 20,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null)
    : IQuery<PagedResult<DeliveryOrderListDto>>;

public class GetDeliveryOrdersQueryHandler : IQueryHandler<GetDeliveryOrdersQuery, PagedResult<DeliveryOrderListDto>>
{
    private readonly DTMS.DeliveryOrder.Application.Projections.IOrderListViewReadRepository _listRepo;

    public GetDeliveryOrdersQueryHandler(
        DTMS.DeliveryOrder.Application.Projections.IOrderListViewReadRepository listRepo)
        => _listRepo = listRepo;

    public async Task<Result<PagedResult<DeliveryOrderListDto>>> Handle(GetDeliveryOrdersQuery request, CancellationToken cancellationToken)
    {
        // Phase P4 — read from the OrderListView projection instead of
        // joining the write-side aggregate. Contract (input → DTO) is
        // unchanged so callers don't notice the swap; new optional
        // filters (HasFailedTrip / HasActiveJob) light up automatically
        // when the caller passes them.
        var filters = new DTMS.DeliveryOrder.Application.Projections.OrderListViewFilters(
            request.Status,
            request.Bucket,
            request.Priority,
            request.TransportMode,
            request.Search,
            request.HasFailedTrip,
            request.HasActiveJob,
            request.SortBy,
            request.SortDescending,
            request.CreatedFromUtc,
            request.CreatedToUtc);

        var (entries, count) = await _listRepo.SearchAsync(filters, request.Page, request.PageSize, cancellationToken);

        var paged = new PagedResult<DeliveryOrderListDto>(
            entries.Select(MapEntryToListDto).ToList(),
            count,
            request.Page,
            request.PageSize);

        return Result<PagedResult<DeliveryOrderListDto>>.Success(paged);
    }

    private static DeliveryOrderListDto MapEntryToListDto(
        DTMS.DeliveryOrder.Application.Projections.OrderListViewEntry e)
    {
        return new DeliveryOrderListDto(
            Id: e.OrderId,
            OrderRef: e.OrderRef,
            // Phase P5 — SourceSystem is now a raw string on the wire.
            // The projection column stores the same lowercase key the
            // entity's SourceSystemKey carries (see the P5 normalization
            // migration 20260630050000 which back-cases legacy PascalCase
            // rows in OrderListView + OrderFacts). No Enum.TryParse.
            SourceSystem: e.SourceSystem,
            SourceSystemDisplayName: e.SourceSystemDisplayName,
            Priority: Enum.TryParse<Priority>(e.Priority, out var pri) ? pri : Domain.Enums.Priority.Normal,
            OrderStatus: Enum.TryParse<OrderStatus>(e.Status, out var st) ? st : OrderStatus.Draft,
            ServiceWindow: e.ServiceWindowLatestUtc.HasValue || e.ServiceWindowEarliestUtc.HasValue
                ? new ServiceWindowDto(e.ServiceWindowEarliestUtc, e.ServiceWindowLatestUtc)
                : null,
            SubmittedAt: e.SubmittedAt,
            CreatedBy: e.CreatedBy,
            RequestedBy: e.RequestedBy,
            Notes: e.Notes,
            CreatedDate: e.CreatedAt,
            UpdatedDate: e.UpdatedAt,
            TotalWeightKg: e.TotalWeightKg,
            TotalQuantity: e.TotalQuantity,
            TotalItems: e.TotalItems,
            RequestedTransportMode: Enum.TryParse<TransportMode>(e.TransportMode, out var tm) ? tm : null,
            RequiresDropPod: e.RequiresDropPod,
            RequiresPickupPod: e.RequiresPickupPod);
    }
}

internal static class DeliveryOrderMapper
{
    public static DeliveryOrderListDto MapToListDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystemKey,
            order.SourceSystemDisplayName,
            order.Priority,
            order.Status,
            order.ServiceWindow is { } sw ? new ServiceWindowDto(sw.EarliestUtc, sw.LatestUtc) : null,
            order.SubmittedAt,
            order.CreatedBy,
            order.RequestedBy,
            order.Notes,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.RequestedTransportMode,
            order.RequiresDropPod,
            order.RequiresPickupPod);

    public static DeliveryOrderDetailDto MapToDetailDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystemKey,
            order.SourceSystemDisplayName,
            order.Priority,
            order.Status,
            order.ServiceWindow is { } sw ? new ServiceWindowDto(sw.EarliestUtc, sw.LatestUtc) : null,
            order.SubmittedAt,
            order.CreatedBy,
            order.RequestedBy,
            order.Notes,
            order.CreatedDate,
            order.UpdatedDate,
            order.TotalWeightKg,
            order.TotalQuantity,
            order.TotalItems,
            order.RequestedTransportMode,
            order.RequiresDropPod,
            order.RequiresPickupPod,
            order.Items.Select(p => new ItemDto(
                p.Id,
                p.ItemSeq,
                p.ItemId,
                p.Description,
                p.PickupLocationCode,
                p.DropLocationCode,
                p.PickupStationId,
                p.DropStationId,
                p.LoadUnitProfileCode,
                p.Dimensions is { } d ? new DimensionsDto(d.LengthMm, d.WidthMm, d.HeightMm, d.VolumeCBM) : null,
                p.WeightKg,
                new QuantityDto(p.Quantity.Value, p.Quantity.Uom),
                p.Hazmat is { } hz ? new HazmatDto(hz.ClassCode, hz.PackingGroup) : null,
                p.Temperature is { } tr ? new TemperatureRangeDto(tr.MinC, tr.MaxC) : null,
                p.HandlingInstructions,
                p.Status,
                p.TripId,
                p.AttemptNumber,
                p.DroppedOffAt,
                p.PickupPod is { } pu ? new PodEventDto(pu.ScannedAt, pu.ScannedBy, pu.Method, pu.Reference) : null,
                p.DropPod   is { } dr ? new PodEventDto(dr.ScannedAt, dr.ScannedBy, dr.Method, dr.Reference) : null
            )).ToList());
}
