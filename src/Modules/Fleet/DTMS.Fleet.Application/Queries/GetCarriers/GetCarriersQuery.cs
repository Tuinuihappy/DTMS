using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Queries.GetCarriers;

/// <summary>Row shape for the registry list. <c>CarrierTypeCode</c> is joined in —
/// the table stores the id (ADR-019) but callers and the UI speak in codes.</summary>
public sealed record CarrierListDto(
    Guid Id,
    string CarrierCode,
    string CarrierTypeCode,
    string? Barcode,
    string? DisplayName,
    string Status,
    string? MaintenanceReason,
    DateTime? MaintenanceSince,
    string? CurrentLocationCode,
    DateTime? LastSeenAt,
    DateTime? CommissionedAt,
    DateTime? RetiredAt,
    string? RetireReason);

/// <summary>
/// Paged, filtered registry list. Uses <see cref="PagedResult{T}"/> from
/// SharedKernel — the same envelope as GetDeliveryOrders / SearchItems — rather
/// than the RIOT3-shaped one Planning uses for action templates, because a
/// carrier is not a vendor concept and borrowing the vendor's wire shape would
/// imply a relationship that does not exist.
/// </summary>
public record GetCarriersQuery(
    string? Status = null,
    string? CarrierTypeCode = null,
    string? Q = null,
    int Page = 1,
    // Literal, not DefaultPageSize: a record's own const is not in scope in its
    // primary constructor's parameter list. Kept in sync by the test below it.
    int PageSize = 25) : IQuery<PagedResult<CarrierListDto>>
{
    /// <summary>25, not the 20 SearchItems uses: the shared Pagination component
    /// offers 10/25/50/100, so 20 is a size no page can actually request and the
    /// documented default would never match real traffic.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Upper bound the existing handlers lack — without it
    /// <c>?pageSize=100000</c> streams the whole table. The UI never exceeds 100.</summary>
    public const int MaxPageSize = 200;
}

public class GetCarriersQueryHandler : IQueryHandler<GetCarriersQuery, PagedResult<CarrierListDto>>
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierTypeRepository _carrierTypes;

    public GetCarriersQueryHandler(ICarrierRepository carriers, ICarrierTypeRepository carrierTypes)
    {
        _carriers = carriers;
        _carrierTypes = carrierTypes;
    }

    public async Task<Result<PagedResult<CarrierListDto>>> Handle(
        GetCarriersQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize is <= 0 or > GetCarriersQuery.MaxPageSize
            ? GetCarriersQuery.DefaultPageSize
            : request.PageSize;

        CarrierStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<CarrierStatus>(request.Status, ignoreCase: true, out var parsed))
                return Result<PagedResult<CarrierListDto>>.Failure(
                    $"Unknown status '{request.Status}'. Use one of: {string.Join(", ", Enum.GetNames<CarrierStatus>())}.");
            status = parsed;
        }

        // One lookup serves both directions: filtering by code (caller → id) and
        // rendering (id → code), so the list never needs a per-row query.
        var typesByCode = (await _carrierTypes.GetAllAsync(cancellationToken))
            .ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);

        Guid? carrierTypeId = null;
        if (!string.IsNullOrWhiteSpace(request.CarrierTypeCode))
        {
            if (!typesByCode.TryGetValue(request.CarrierTypeCode.Trim(), out var type))
                return Result<PagedResult<CarrierListDto>>.Failure(
                    $"CarrierType '{request.CarrierTypeCode.Trim().ToUpperInvariant()}' not found.");
            carrierTypeId = type.Id;
        }

        var typeCodeById = typesByCode.Values.ToDictionary(t => t.Id, t => t.Code);

        var (rows, totalCount) = await _carriers.SearchAsync(
            status, carrierTypeId, request.Q, page, pageSize, cancellationToken);

        var data = rows.Select(c => new CarrierListDto(
            c.Id,
            c.CarrierCode,
            typeCodeById.TryGetValue(c.CarrierTypeId, out var code) ? code : string.Empty,
            c.Barcode,
            c.DisplayName,
            c.Status.ToString(),
            c.MaintenanceReason,
            c.MaintenanceSince,
            c.CurrentLocationCode,
            c.LastSeenAt,
            c.CommissionedAt,
            c.RetiredAt,
            c.RetireReason)).ToList();

        return Result<PagedResult<CarrierListDto>>.Success(
            new PagedResult<CarrierListDto>(data, totalCount, page, pageSize));
    }
}
