using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DTMS.Api.Infrastructure.Health;

// JSON writer for /health/ready and /health/vendors.
//
// Each check already says how bad its failure is: postgres, rabbitmq and
// drain report Unhealthy (this instance can't, or mustn't, take traffic);
// redis and riot3 report Degraded (it still serves, with less). The HTTP
// status has to keep that distinction. The previous writer sent 503 for
// anything not Healthy, so a Redis outage made the container healthcheck
// (curl -f) mark a working API unhealthy, and portproxy-api / rebuild /
// server-install waited on it. Degraded now answers 200; the body still
// says "Degraded" and names the failing check.
internal static class HealthResponse
{
    public static int StatusCodeFor(HealthStatus status) =>
        status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

    public static Task WriteJsonAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = StatusCodeFor(report.Status);
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name   = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                error  = e.Value.Exception?.Message
            })
        });
        return ctx.Response.WriteAsync(result);
    }
}
