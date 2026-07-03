using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Wms.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DTMS.Api.SystemCapabilities;

/// <summary>
/// WMS PR-4 — surfaces per-deployment feature flags to the frontend so
/// it can conditionally show transport mode options and the WMS picker.
///
/// Kept anonymous (no auth) so the login page / capabilities probe can
/// call it before a JWT is available.
/// </summary>
public static class SystemCapabilitiesEndpoints
{
    public static void MapSystemCapabilitiesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/system/capabilities",
            (IConfiguration configuration, IOptions<WmsOptions> wmsOptions) =>
        {
            var wms = wmsOptions.Value;
            var enabledModes = new List<string>();
            foreach (var mode in Enum.GetValues<TransportMode>())
            {
                var key = $"TransportModes:{mode}:Enabled";
                if (configuration.GetValue<bool>(key))
                    enabledModes.Add(mode.ToString());
            }

            return Results.Ok(new SystemCapabilitiesDto(
                WmsEnabled: wms.Enabled,
                EnabledTransportModes: enabledModes));
        }).WithTags("System");
    }
}

public sealed record SystemCapabilitiesDto(
    bool WmsEnabled,
    IReadOnlyList<string> EnabledTransportModes);
