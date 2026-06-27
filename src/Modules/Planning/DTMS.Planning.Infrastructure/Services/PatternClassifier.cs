using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Infrastructure.Services;

/// <summary>
/// Classifies delivery patterns based on order structure.
/// </summary>
public class PatternClassifier : IPatternClassifier
{
    private readonly ILogger<PatternClassifier> _logger;

    public PatternClassifier(ILogger<PatternClassifier> logger)
    {
        _logger = logger;
    }

    public PatternClassification Classify(List<OrderInfo> orders)
    {
        if (orders.Count == 0)
            throw new ArgumentException("At least one order is required.");

        // Multiple orders → Consolidation
        if (orders.Count > 1)
        {
            _logger.LogInformation("Classified {Count} orders as Consolidation", orders.Count);
            return new PatternClassification(PatternType.Consolidation, orders);
        }

        var order = orders[0];

        // Single order with multiple drops → MultiStop
        if (order.DropStationIds.Count > 1)
        {
            _logger.LogInformation("Classified order {OrderId} as MultiStop ({Drops} drops)",
                order.OrderId, order.DropStationIds.Count);
            return new PatternClassification(PatternType.MultiStop, orders);
        }

        // Single order, single drop → PointToPoint
        _logger.LogInformation("Classified order {OrderId} as PointToPoint", order.OrderId);
        return new PatternClassification(PatternType.PointToPoint, orders);
    }
}
