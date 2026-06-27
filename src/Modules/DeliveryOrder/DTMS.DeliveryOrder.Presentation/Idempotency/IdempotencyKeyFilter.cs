using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DTMS.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Best-effort endpoint filter for <c>Idempotency-Key</c> on mutation endpoints.
/// <list type="bullet">
///   <item>Missing header → pass through (no caching, no enforcement) — clients
///         that opt out accept the risk of duplicate writes on network retries.</item>
///   <item>Same key + same request body → replay cached response, set
///         <c>Idempotency-Replayed: true</c>.</item>
///   <item>Same key + different body → 422 Unprocessable Entity.</item>
/// </list>
/// Apply via <c>RequireIdempotencyKeyExtensions.RequireIdempotencyKey()</c>.
/// </summary>
public sealed class IdempotencyKeyFilter : IEndpointFilter
{
    private const string HeaderName = "Idempotency-Key";
    private const string CacheKeyPrefix = "idempotency:v2:";

    private readonly IDistributedCache _cache;
    private readonly ILogger<IdempotencyKeyFilter> _logger;

    public IdempotencyKeyFilter(IDistributedCache cache, ILogger<IdempotencyKeyFilter> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(key))
        {
            // Best-effort mode: client opted out of idempotency. Execute normally
            // with no replay protection — duplicates on retry are the caller's risk.
            return await next(context);
        }

        if (key.Length > 200)
        {
            return Results.Problem(
                title: "Idempotency-Key too long",
                detail: "Idempotency-Key must be 200 characters or fewer.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var hash = IdempotencyHasher.Compute(
            http.Request.Method,
            http.Request.Path.Value ?? string.Empty,
            context.Arguments);

        var cacheKey = CacheKeyPrefix + key;
        var cached = await _cache.GetStringAsync(cacheKey, http.RequestAborted);

        if (cached is not null)
        {
            IdempotencyCacheEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<IdempotencyCacheEntry>(cached);
            }
            catch (JsonException)
            {
                // Stale shape from a previous deploy — fall through and re-execute.
                _logger.LogWarning("Stale idempotency cache entry shape for key {Key}; re-executing.", key);
            }

            if (entry is not null)
            {
                if (entry.RequestHash != hash)
                {
                    _logger.LogWarning("Idempotency-Key conflict for {Key}: request body differs from prior call.", key);
                    return Results.Problem(
                        title: "Idempotency-Key conflict",
                        detail: "The same Idempotency-Key was used with a different request body. Use a new key for a different request.",
                        statusCode: StatusCodes.Status422UnprocessableEntity);
                }

                _logger.LogInformation("Replaying cached response for Idempotency-Key {Key} (status {Status}).", key, entry.StatusCode);
                return new CachedReplayResult(entry);
            }
        }

        var result = await next(context);
        if (result is IResult inner)
        {
            return new CachingResultWrapper(inner, _cache, cacheKey, hash);
        }

        // Endpoint didn't return an IResult (rare in minimal-API mutation handlers) —
        // let ASP.NET process the value as-is; we just don't cache it.
        return result;
    }
}
