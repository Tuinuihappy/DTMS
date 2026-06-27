namespace DTMS.DeliveryOrder.Application.Options;

public class DeliveryOrderOptions
{
    public const string SectionName = "DeliveryOrder";
    public int MinimumSlaLeadTimeMinutes { get; set; } = 30;

    /// <summary>
    /// Fallback weight (kg) used when an item is confirmed without a known
    /// WeightKg. Set this conservatively — Planning will pick a vehicle large
    /// enough to carry this much. Default 500 kg covers most AMR fleets.
    /// </summary>
    public double WeightFallbackKg { get; set; } = 500.0;
}
