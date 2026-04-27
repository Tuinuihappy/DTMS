namespace AMR.DeliveryPlanning.DeliveryOrder.Application.Options;

public class DeliveryOrderOptions
{
    public const string SectionName = "DeliveryOrder";
    public int MinimumSlaLeadTimeMinutes { get; set; } = 30;
}
