namespace DTMS.Api.VendorHealth;

public interface IRiot3HealthProbe
{
    Task<ProbeOutcome> ProbeAsync(CancellationToken ct);
}
