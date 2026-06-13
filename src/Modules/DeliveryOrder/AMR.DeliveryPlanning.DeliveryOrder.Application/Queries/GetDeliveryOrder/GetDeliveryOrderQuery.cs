using System.Text.Json;
using System.Text.Json.Serialization;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.DeliveryOrder.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using Microsoft.Extensions.Caching.Distributed;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Queries.GetDeliveryOrder;

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

public record DeliveryOrderListDto(
    Guid Id,
    string OrderRef,
    SourceSystem SourceSystem,
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
    SourceSystem SourceSystem,
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
    int PageSize = 20)
    : IQuery<PagedResult<DeliveryOrderListDto>>;

public class GetDeliveryOrdersQueryHandler : IQueryHandler<GetDeliveryOrdersQuery, PagedResult<DeliveryOrderListDto>>
{
    private readonly AMR.DeliveryPlanning.DeliveryOrder.Application.Projections.IOrderListViewReadRepository _listRepo;

    public GetDeliveryOrdersQueryHandler(
        AMR.DeliveryPlanning.DeliveryOrder.Application.Projections.IOrderListViewReadRepository listRepo)
        => _listRepo = listRepo;

    public async Task<Result<PagedResult<DeliveryOrderListDto>>> Handle(GetDeliveryOrdersQuery request, CancellationToken cancellationToken)
    {
        // Phase P4 — read from the OrderListView projection instead of
        // joining the write-side aggregate. Contract (input → DTO) is
        // unchanged so callers don't notice the swap; new optional
        // filters (HasFailedTrip / HasActiveJob) light up automatically
        // when the caller passes them.
        var filters = new AMR.DeliveryPlanning.DeliveryOrder.Application.Projections.OrderListViewFilters(
            request.Status,
            request.Bucket,
            request.Priority,
            request.TransportMode,
            request.Search,
            request.HasFailedTrip,
            request.HasActiveJob,
            request.SortBy,
            request.SortDescending);

        var (entries, count) = await _listRepo.SearchAsync(filters, request.Page, request.PageSize, cancellationToken);

        var paged = new PagedResult<DeliveryOrderListDto>(
            entries.Select(MapEntryToListDto).ToList(),
            count,
            request.Page,
            request.PageSize);

        return Result<PagedResult<DeliveryOrderListDto>>.Success(paged);
    }

    private static DeliveryOrderListDto MapEntryToListDto(
        AMR.DeliveryPlanning.DeliveryOrder.Application.Projections.OrderListViewEntry e)
    {
        return new DeliveryOrderListDto(
            Id: e.OrderId,
            OrderRef: e.OrderRef,
            SourceSystem: Enum.TryParse<SourceSystem>(e.SourceSystem, out var src) ? src : SourceSystem.Manual,
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

public record DeliveryOrderStatsDto(
    int Total,
    int Active,
    int Completed,
    double TotalWeightKg,
    Dictionary<OrderStatus, int> ByStatus);

public record GetDeliveryOrderStatsQuery() : IQuery<DeliveryOrderStatsDto>;

public class GetDeliveryOrderStatsQueryHandler : IQueryHandler<GetDeliveryOrderStatsQuery, DeliveryOrderStatsDto>
{
    // Short TTL — stale up to 5s is acceptable for an ops dashboard
    // (humans don't perceive sub-5s lag) and one DB hit per 5s no
    // matter how many users are polling crushes the load curve when
    // there are 10+ tabs open in a control room.
    private const string CacheKey = "stats:delivery-orders:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        // Enum keys in Dictionary<OrderStatus, int> need a string
        // converter to roundtrip cleanly through the cache layer.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IDeliveryOrderRepository _repo;
    private readonly IDistributedCache _cache;

    public GetDeliveryOrderStatsQueryHandler(IDeliveryOrderRepository repo, IDistributedCache cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<Result<DeliveryOrderStatsDto>> Handle(GetDeliveryOrderStatsQuery request, CancellationToken cancellationToken)
    {
        // Cache lookup — best-effort. Any deserialization issue silently
        // falls through to a fresh DB query rather than failing the call.
        var cached = await _cache.GetStringAsync(CacheKey, cancellationToken);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var hit = JsonSerializer.Deserialize<DeliveryOrderStatsDto>(cached, CacheJsonOptions);
                if (hit is not null)
                    return Result<DeliveryOrderStatsDto>.Success(hit);
            }
            catch (JsonException)
            {
                // Schema drift across deployments — proceed to refill.
            }
        }

        var stats = await _repo.GetStatsAsync(cancellationToken);

        var active = OrderStatusBuckets.Active.Sum(s => stats.ByStatus.GetValueOrDefault(s));
        var completed = OrderStatusBuckets.Completed.Sum(s => stats.ByStatus.GetValueOrDefault(s));

        // Ensure every status appears in the response (with 0 if absent)
        // so the frontend chip counts don't render "undefined".
        var byStatus = Enum.GetValues<OrderStatus>().ToDictionary(s => s, s => stats.ByStatus.GetValueOrDefault(s));

        var dto = new DeliveryOrderStatsDto(stats.Total, active, completed, stats.TotalWeightKg, byStatus);

        try
        {
            await _cache.SetStringAsync(
                CacheKey,
                JsonSerializer.Serialize(dto, CacheJsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);
        }
        catch
        {
            // Cache write is best-effort — Redis blip shouldn't fail the API.
        }

        return Result<DeliveryOrderStatsDto>.Success(dto);
    }
}

internal static class DeliveryOrderMapper
{
    public static DeliveryOrderListDto MapToListDto(Domain.Entities.DeliveryOrder order) =>
        new(
            order.Id,
            order.OrderRef,
            order.SourceSystem,
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
            order.SourceSystem,
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
                new QuantityDto(p.Quantity.Value, p.Quantity.Uom.ToString()),
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
