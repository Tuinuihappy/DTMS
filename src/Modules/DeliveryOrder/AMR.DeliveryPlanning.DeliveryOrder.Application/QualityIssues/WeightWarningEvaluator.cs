using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.QualityIssues;

/// <summary>
/// Inspects an order's items and emits a warning for each one that lacks a
/// <c>WeightKg</c> value. The system will fall back to a configured default
/// when publishing the planning event (so capacity planning still works),
/// but the warning makes the missing data explicit to the caller and to audit.
/// </summary>
public static class WeightWarningEvaluator
{
    public static IReadOnlyList<OrderQualityIssue> Evaluate(IEnumerable<Item> items)
    {
        var warnings = new List<OrderQualityIssue>();
        foreach (var item in items.OrderBy(i => i.ItemSeq))
        {
            if (item.WeightKg is null or <= 0)
            {
                warnings.Add(new OrderQualityIssue(
                    QualityIssueCodes.ItemWeightMissing,
                    QualityIssueSeverity.Warning,
                    $"items[seq={item.ItemSeq}].weightKg",
                    $"Item seq {item.ItemSeq} (itemId={item.ItemId}) has no weight; load planning will use the configured fallback."));
            }
        }
        return warnings;
    }
}
