using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.Facility.Domain.ValueObjects;

/// <summary>
/// Per-day operating window for a warehouse. Used to reject dispatches
/// outside of receiving hours (the Manual SLA watchdog reads this when
/// computing expectedPickupBy — a trip arriving at 23:00 to a warehouse
/// that closes at 18:00 should fail validation, not just stall).
///
/// Modelled as 7 day-of-week entries — simpler than a windows-list
/// approach and matches how dispatchers think about warehouses
/// ("WH-BKK-01 is open Mon-Fri 08-18, weekends closed"). 24/7 operation
/// is represented by all days having 00:00 → 23:59:59.
/// </summary>
public class OperatingHours : ValueObject
{
    public TimeSpan? MondayOpen { get; private set; }
    public TimeSpan? MondayClose { get; private set; }
    public TimeSpan? TuesdayOpen { get; private set; }
    public TimeSpan? TuesdayClose { get; private set; }
    public TimeSpan? WednesdayOpen { get; private set; }
    public TimeSpan? WednesdayClose { get; private set; }
    public TimeSpan? ThursdayOpen { get; private set; }
    public TimeSpan? ThursdayClose { get; private set; }
    public TimeSpan? FridayOpen { get; private set; }
    public TimeSpan? FridayClose { get; private set; }
    public TimeSpan? SaturdayOpen { get; private set; }
    public TimeSpan? SaturdayClose { get; private set; }
    public TimeSpan? SundayOpen { get; private set; }
    public TimeSpan? SundayClose { get; private set; }

    private OperatingHours() { }

    /// <summary>
    /// Always-open default — useful for 24/7 distribution centres and
    /// for warehouses without configured hours (no-enforcement = open).
    /// </summary>
    public static OperatingHours AlwaysOpen()
    {
        var open = TimeSpan.Zero;
        var close = new TimeSpan(23, 59, 59);
        return new OperatingHours
        {
            MondayOpen = open, MondayClose = close,
            TuesdayOpen = open, TuesdayClose = close,
            WednesdayOpen = open, WednesdayClose = close,
            ThursdayOpen = open, ThursdayClose = close,
            FridayOpen = open, FridayClose = close,
            SaturdayOpen = open, SaturdayClose = close,
            SundayOpen = open, SundayClose = close,
        };
    }

    /// <summary>
    /// Uniform weekday + weekend windows — common "08:00-18:00 weekdays,
    /// 09:00-13:00 Saturday, closed Sunday" pattern.
    /// </summary>
    public static OperatingHours Standard(
        TimeSpan weekdayOpen, TimeSpan weekdayClose,
        TimeSpan? saturdayOpen = null, TimeSpan? saturdayClose = null,
        TimeSpan? sundayOpen = null, TimeSpan? sundayClose = null)
    {
        Validate(weekdayOpen, weekdayClose, nameof(weekdayOpen));
        if (saturdayOpen.HasValue && saturdayClose.HasValue)
            Validate(saturdayOpen.Value, saturdayClose.Value, nameof(saturdayOpen));
        if (sundayOpen.HasValue && sundayClose.HasValue)
            Validate(sundayOpen.Value, sundayClose.Value, nameof(sundayOpen));

        return new OperatingHours
        {
            MondayOpen = weekdayOpen, MondayClose = weekdayClose,
            TuesdayOpen = weekdayOpen, TuesdayClose = weekdayClose,
            WednesdayOpen = weekdayOpen, WednesdayClose = weekdayClose,
            ThursdayOpen = weekdayOpen, ThursdayClose = weekdayClose,
            FridayOpen = weekdayOpen, FridayClose = weekdayClose,
            SaturdayOpen = saturdayOpen, SaturdayClose = saturdayClose,
            SundayOpen = sundayOpen, SundayClose = sundayClose,
        };
    }

    /// <summary>
    /// True if the given time-of-day falls within the configured window
    /// for that day. Days with null open/close are treated as closed.
    /// </summary>
    public bool IsOpenAt(DateTime dateTimeLocal)
    {
        var (open, close) = dateTimeLocal.DayOfWeek switch
        {
            DayOfWeek.Monday    => (MondayOpen, MondayClose),
            DayOfWeek.Tuesday   => (TuesdayOpen, TuesdayClose),
            DayOfWeek.Wednesday => (WednesdayOpen, WednesdayClose),
            DayOfWeek.Thursday  => (ThursdayOpen, ThursdayClose),
            DayOfWeek.Friday    => (FridayOpen, FridayClose),
            DayOfWeek.Saturday  => (SaturdayOpen, SaturdayClose),
            _                   => (SundayOpen, SundayClose),
        };

        if (open is null || close is null) return false;

        var t = dateTimeLocal.TimeOfDay;
        return t >= open.Value && t <= close.Value;
    }

    private static void Validate(TimeSpan open, TimeSpan close, string paramName)
    {
        if (open < TimeSpan.Zero || open >= TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(paramName, "Open time must be within [00:00, 24:00)");
        if (close <= open)
            throw new ArgumentException("Close time must be after open time", paramName);
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return MondayOpen ?? TimeSpan.MinValue;
        yield return MondayClose ?? TimeSpan.MinValue;
        yield return TuesdayOpen ?? TimeSpan.MinValue;
        yield return TuesdayClose ?? TimeSpan.MinValue;
        yield return WednesdayOpen ?? TimeSpan.MinValue;
        yield return WednesdayClose ?? TimeSpan.MinValue;
        yield return ThursdayOpen ?? TimeSpan.MinValue;
        yield return ThursdayClose ?? TimeSpan.MinValue;
        yield return FridayOpen ?? TimeSpan.MinValue;
        yield return FridayClose ?? TimeSpan.MinValue;
        yield return SaturdayOpen ?? TimeSpan.MinValue;
        yield return SaturdayClose ?? TimeSpan.MinValue;
        yield return SundayOpen ?? TimeSpan.MinValue;
        yield return SundayClose ?? TimeSpan.MinValue;
    }
}
