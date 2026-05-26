namespace AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;

/// <summary>
/// Physical / mechanical handling rules an AMR (or a human escort) must
/// follow when carrying an item. Distinct from <see cref="ValueObjects.HazmatInfo"/>
/// (regulatory chemical hazard) and <see cref="ValueObjects.TemperatureRange"/>
/// (climate constraint) — those answer different questions. New codes can
/// be added here as physical rules expand; the closed set keeps the
/// Planning solver's pattern-matching exhaustive.
/// </summary>
public enum HandlingInstruction
{
    /// <summary>Breakable — reduce AMR speed and smooth acceleration.</summary>
    Fragile,
    /// <summary>Orientation-sensitive — AMR must not tilt; flat-deck required.</summary>
    ThisSideUp,
    /// <summary>Cannot bear weight on top — allocate its own shelf, no stacking.</summary>
    DoNotStack,
    /// <summary>Mass requires 2-person lift — autonomous pick disallowed; human escort needed.</summary>
    HeavyLift,
    /// <summary>Cut hazard — gripper guard required.</summary>
    Sharp,
    /// <summary>Moisture-sensitive — route avoids wet / outdoor corridors.</summary>
    KeepDry,
    /// <summary>Light-sensitive (chemicals, photographic media) — opaque carrier.</summary>
    KeepDark,
    /// <summary>Pinch / finger-trap risk — slow open/close at pickup and drop.</summary>
    PinchHazard
}
