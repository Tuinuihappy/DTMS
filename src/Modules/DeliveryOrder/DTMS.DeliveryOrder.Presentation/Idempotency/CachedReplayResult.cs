using Microsoft.AspNetCore.Http;

namespace DTMS.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Replays a previously cached mutation response when the same Idempotency-Key
/// is presented with the same request body. Adds <c>Idempotency-Replayed: true</c>
/// so clients can tell when a server-side retry path was taken.
/// </summary>
internal sealed class CachedReplayResult : IResult
{
    private readonly IdempotencyCacheEntry _entry;

    public CachedReplayResult(IdempotencyCacheEntry entry) => _entry = entry;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = _entry.StatusCode;
        if (!string.IsNullOrEmpty(_entry.ContentType))
            httpContext.Response.ContentType = _entry.ContentType;
        httpContext.Response.Headers["Idempotency-Replayed"] = "true";

        var body = Convert.FromBase64String(_entry.BodyBase64);
        if (body.Length > 0)
            await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted);
    }
}
