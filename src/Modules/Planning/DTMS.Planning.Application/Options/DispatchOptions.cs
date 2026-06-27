namespace AMR.DeliveryPlanning.Planning.Application.Options;

/// <summary>
/// Planning-side dispatch behaviour switches. The envelope-dispatch flag
/// gates the new RIOT3 OrderTemplate-based path; when off, Planning keeps
/// driving the legacy job/leg/task pipeline.
/// </summary>
public class DispatchOptions
{
    public const string SectionName = "Dispatch";

    /// <summary>
    /// When true, DeliveryOrderValidatedConsumer tries to dispatch via
    /// OrderTemplate (POST /api/v4/orders to RIOT3) for each station-pair
    /// group before falling back to the legacy job/leg/task pipeline.
    /// Requires an active OrderTemplate registered against the route.
    /// </summary>
    public bool UseOrderTemplateDispatch { get; set; } = false;
}
