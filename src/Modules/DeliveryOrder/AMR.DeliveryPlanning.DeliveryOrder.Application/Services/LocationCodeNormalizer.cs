namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Services;

/// <summary>
/// Resolves Item.PickupLocationCode / DropLocationCode values (which accept either a
/// station Guid or a human-readable Code like "SHELF5") to their canonical Code, so the
/// order entity always stores the Code form when one exists. Unknown inputs are passed
/// through unchanged — submit-time validation will reject them with a clear error.
/// </summary>
public static class LocationCodeNormalizer
{
    /// <summary>
    /// Pre-resolves a batch of location inputs in one query and returns a normalizer
    /// function that callers apply per-item just before <c>DeliveryOrder.AddItem</c>.
    /// </summary>
    public static async Task<Func<string, string>> BuildAsync(
        IEnumerable<string> locationInputs,
        IStationLookup lookup,
        CancellationToken ct)
    {
        var distinct = locationInputs
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
            return s => s;

        var resolved = await lookup.ResolveBatchAsync(distinct, ct);

        return input =>
            resolved.TryGetValue(input, out var s) && !string.IsNullOrWhiteSpace(s.Code)
                ? s.Code
                : input;
    }
}
