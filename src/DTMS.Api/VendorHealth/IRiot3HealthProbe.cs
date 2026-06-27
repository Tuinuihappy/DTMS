namespace AMR.DeliveryPlanning.Api.VendorHealth;

public interface IRiot3HealthProbe
{
    Task<ProbeOutcome> ProbeAsync(CancellationToken ct);
}
