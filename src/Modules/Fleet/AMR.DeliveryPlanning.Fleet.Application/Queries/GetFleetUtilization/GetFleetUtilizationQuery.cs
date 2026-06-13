using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Fleet.Application.Queries.GetFleetUtilization;

public record GetFleetUtilizationQuery(DateTime FromUtc, DateTime ToUtc) : IQuery<FleetUtilizationResponse>;

public record FleetUtilizationBucketDto(
    DateTime BucketHour,
    int Active,
    int Busy,
    int Idle,
    int Charging,
    int Maintenance,
    int LowBattery,
    int Offline,
    int Total);

public record FleetUtilizationResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<FleetUtilizationBucketDto> Buckets,
    FleetUtilizationBucketDto? Latest,
    DateTime? LastEventAt);
