using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Planning.Domain.Entities;

public enum MissionType { Move, Act }

// One mission inside an OrderTemplate. Stored as part of the parent's jsonb
// missions array. Three shapes:
//   1. MOVE — type=Move, mapId + stationId required
//   2. ACT inline — type=Act with ActionType + ActionParameters set
//   3. ACT by reference — type=Act with ActionTemplateName set; dispatcher
//      resolves it against the ActionTemplate catalog at runtime
//
// Validation enforces exactly one of inline/reference for Act missions so the
// dispatcher never has to guess.
public sealed class OrderTemplateMission : ValueObject
{
    public int Sequence { get; private set; }
    public MissionType Type { get; private set; }
    public string Category { get; private set; } = "agv";

    // MOVE-specific
    public int? MapId { get; private set; }
    public int? StationId { get; private set; }

    // ACT-specific (inline path) — null when using ActionTemplateName
    public string? ActionType { get; private set; }
    public string? BlockingType { get; private set; }
    public IReadOnlyList<MissionActionParameter>? ActionParameters { get; private set; }

    // ACT-specific (reference path) — null when using inline params
    public string? ActionTemplateName { get; private set; }

    private OrderTemplateMission() { } // EF Core

    // System.Text.Json (used by the jsonb value converter) ignores private
    // factory methods — give it a constructor it can target instead. Also
    // used when EF Core hydrates the entity through reflection.
    [System.Text.Json.Serialization.JsonConstructor]
    internal OrderTemplateMission(
        int sequence,
        MissionType type,
        string category,
        int? mapId,
        int? stationId,
        string? actionType,
        string? blockingType,
        IReadOnlyList<MissionActionParameter>? actionParameters,
        string? actionTemplateName)
    {
        Sequence = sequence;
        Type = type;
        Category = category;
        MapId = mapId;
        StationId = stationId;
        ActionType = actionType;
        BlockingType = blockingType;
        ActionParameters = actionParameters;
        ActionTemplateName = actionTemplateName;
    }

    public static OrderTemplateMission CreateMove(int sequence, string category, int mapId, int stationId)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category required.", nameof(category));
        return new OrderTemplateMission
        {
            Sequence = sequence,
            Type = MissionType.Move,
            Category = category.Trim(),
            MapId = mapId,
            StationId = stationId
        };
    }

    public static OrderTemplateMission CreateActInline(
        int sequence,
        string category,
        string actionType,
        string blockingType,
        IEnumerable<MissionActionParameter> actionParameters)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category required.", nameof(category));
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("ActionType required for inline ACT mission.", nameof(actionType));
        if (string.IsNullOrWhiteSpace(blockingType))
            throw new ArgumentException("BlockingType required.", nameof(blockingType));
        var paramList = (actionParameters ?? Array.Empty<MissionActionParameter>()).ToList();
        return new OrderTemplateMission
        {
            Sequence = sequence,
            Type = MissionType.Act,
            Category = category.Trim(),
            ActionType = actionType.Trim(),
            BlockingType = blockingType.Trim(),
            ActionParameters = paramList
        };
    }

    public static OrderTemplateMission CreateActByReference(
        int sequence,
        string category,
        string actionTemplateName,
        string? blockingType = null)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category required.", nameof(category));
        if (string.IsNullOrWhiteSpace(actionTemplateName))
            throw new ArgumentException("ActionTemplateName required for reference ACT mission.", nameof(actionTemplateName));
        return new OrderTemplateMission
        {
            Sequence = sequence,
            Type = MissionType.Act,
            Category = category.Trim(),
            ActionTemplateName = actionTemplateName.Trim(),
            BlockingType = string.IsNullOrWhiteSpace(blockingType) ? "NONE" : blockingType.Trim()
        };
    }

    internal OrderTemplateMission WithSequence(int newSequence)
    {
        return new OrderTemplateMission
        {
            Sequence = newSequence,
            Type = Type,
            Category = Category,
            MapId = MapId,
            StationId = StationId,
            ActionType = ActionType,
            BlockingType = BlockingType,
            ActionParameters = ActionParameters,
            ActionTemplateName = ActionTemplateName
        };
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Sequence;
        yield return Type;
        yield return Category;
        if (MapId.HasValue) yield return MapId.Value;
        if (StationId.HasValue) yield return StationId.Value;
        if (ActionType is not null) yield return ActionType;
        if (BlockingType is not null) yield return BlockingType;
        if (ActionTemplateName is not null) yield return ActionTemplateName;
        if (ActionParameters is not null)
        {
            foreach (var p in ActionParameters)
            {
                yield return p.Key;
                yield return p.Value ?? "<null>";
            }
        }
    }
}

// Mirrors one entry in the RIOT3 actionParameters array. Value is stored as
// the raw string the operator typed; the dispatcher will re-serialize as
// integer or string into the outgoing RIOT3 payload depending on the param key.
public sealed class MissionActionParameter : ValueObject
{
    public string Key { get; private set; } = string.Empty;
    public string? Value { get; private set; }

    private MissionActionParameter() { } // EF Core / JSON deserialization

    // Public constructor doubles as the JSON deserialization target so
    // entries survive a round-trip through jsonb intact.
    [System.Text.Json.Serialization.JsonConstructor]
    public MissionActionParameter(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Parameter key required.", nameof(key));
        Key = key.Trim();
        Value = value;
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Key;
        yield return Value ?? "<null>";
    }
}
