using DTMS.Facility.Application.Commands.AddStation;
using DTMS.Facility.Domain.ValueObjects;

namespace DTMS.Facility.Application.Commands;

// Tiny adapter so AddStation and UpdateStation handlers don't both repeat
// the Input → ValueObject loop. Keeps the Application-layer DTO
// (StationActionInput) and the Domain value object (StationAction) on
// opposite sides of a single conversion call.
internal static class StationActionInputMapper
{
    public static Dictionary<string, StationAction>? ToDomain(
        IDictionary<string, StationActionInput>? inputs)
    {
        if (inputs is null || inputs.Count == 0)
            return null;

        var result = new Dictionary<string, StationAction>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in inputs)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
                continue;

            result[kv.Key.Trim()] = new StationAction(
                kv.Value.ActionType,
                kv.Value.Category,
                kv.Value.Parameters);
        }
        return result.Count == 0 ? null : result;
    }
}
