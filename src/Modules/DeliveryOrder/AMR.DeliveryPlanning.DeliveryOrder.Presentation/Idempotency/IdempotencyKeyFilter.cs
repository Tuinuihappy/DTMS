using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Endpoint filter that enforces <c>Idempotency-Key</c> on mutation endpoints.
/// <list type="bullet">
///   <item>Missing header → 400 Bad Request.</item>
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
            return Results.Problem(
                title: "Idempotency-Key required",
                detail: $"The '{HeaderName}' header is required for mutation requests. Send a unique value (UUID recommended) per logical operation; retries with the same key replay the original response.",
                statusCode: StatusCodes.Status400BadRequest);
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
