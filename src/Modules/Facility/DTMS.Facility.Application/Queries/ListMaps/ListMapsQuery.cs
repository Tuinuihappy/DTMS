using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Queries.ListMaps;

public record ListMapsQuery() : IQuery<IReadOnlyList<MapSummaryDto>>;

public record MapSummaryDto(
    Guid Id,
    string Name,
    string Version,
    double Width,
    double Height,
    string? VendorRef,
    int StationCount,
    int ActiveStationCount);
