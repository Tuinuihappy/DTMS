namespace AMR.DeliveryPlanning.Planning.Domain.Services;

public record OrderInfo(
    Guid OrderId,
    Guid PickupStationId,
    List<Guid> DropStationIds,
    string? DestZone,
    DateTime? SlaDeadline,
    double TotalWeight,
    string? RequiredCapability);

public interface IPatternClassifier
{
    PatternClassification Classify(List<OrderInfo> orders);
}

public record PatternClassification(
    Enums.PatternType Pattern,
    List<OrderInfo> GroupedOrders);
