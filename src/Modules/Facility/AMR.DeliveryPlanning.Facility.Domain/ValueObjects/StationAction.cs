using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.ValueObjects;

// One executable action a robot performs at a station — keyed in
// Station.Actions by an "intent" string (e.g. "lift", "drop", "charge",
// "scan"). The Dispatch module reads the intent from the trip's task
// type and looks it up here to compose the matching RIOT3 ACT mission.
//
// Vendor-agnostic on purpose: ActionType / Category / Parameters are
// strings the vendor adapter knows how to interpret. For RIOT3 today
// that means actionType="standardRobotsCustom", category="agv", and a
// {id, param0, param1, ...} parameter map.
public sealed class StationAction : ValueObject
{
    public string ActionType { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, string>? Parameters { get; private set; }

    private StationAction() { }

    public StationAction(string actionType, string? category = null, IDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("ActionType must not be empty.", nameof(actionType));

        ActionType = actionType.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? "agv" : category.Trim();
        Parameters = parameters is null || parameters.Count == 0
            ? null
            : new Dictionary<string, string>(parameters, StringComparer.Ordinal);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ActionType;
        yield return Category;
        if (Parameters is not null)
        {
            foreach (var kv in Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                yield return kv.Key;
                yield return kv.Value;
            }
        }
    }
}
