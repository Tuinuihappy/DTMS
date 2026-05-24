using AMR.DeliveryPlanning.DeliveryOrder.Domain.Entities;

namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Commands.SubmitDeliveryOrder;

/// <summary>
/// Entity-level submit-readiness check used by /submit endpoint to verify a draft
/// has all fields a strict submit requires. Mirrors the rules in
/// <c>SubmitItemDtoValidator</c> but works on the persisted entity.
/// </summary>
internal static class SubmitReadinessCheck
{
    public static (bool IsValid, string Error) Check(Domain.Entities.DeliveryOrder order)
    {
        var errors = new List<string>();

        if (order.Items.Count == 0)
            errors.Add("At least one item is required.");

        foreach (var item in order.Items)
        {
            var prefix = $"Item seq {item.ItemSeq}";

            if (string.IsNullOrWhiteSpace(item.Sku))
                errors.Add($"{prefix}: Sku is required.");
            if (item.PickupLocation is null)
                errors.Add($"{prefix}: PickupLocation is required.");
            if (item.DropLocation is null)
                errors.Add($"{prefix}: DropLocation is required.");
            if (item.PickupLocation is not null && item.DropLocation is not null
                && item.PickupLocation == item.DropLocation)
                errors.Add($"{prefix}: Pickup and Drop locations must be different.");
            if (item.Quantity <= 0)
                errors.Add($"{prefix}: Quantity must be > 0.");
            if (string.IsNullOrWhiteSpace(item.Uom))
                errors.Add($"{prefix}: Uom is required.");
        }

        return (errors.Count == 0, string.Join("; ", errors));
    }
}
