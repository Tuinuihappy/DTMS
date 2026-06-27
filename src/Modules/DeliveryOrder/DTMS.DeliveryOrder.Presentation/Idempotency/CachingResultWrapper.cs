using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace DTMS.DeliveryOrder.Presentation.Idempotency;

/// <summary>
/// Wraps an inner <see cref="IResult"/>, captures the response body bytes by
/// temporarily swapping <see cref="HttpResponse.Body"/> with a memory stream,
/// then stores the result under <c>idempotency:v2:{key}</c> so subsequent
/// requests with the same Idempotency-Key replay the same response.
/// Only 2xx and 4xx responses are cached — 5xx are left for the client to retry.
/// </summary>
internal sealed class CachingResultWrapper : IResult
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly IResult _inner;
    private readonly IDistributedCache _cache;
    private readonly string _cacheKey;
    private readonly string _requestHash;

    public CachingResultWrapper(IResult inner, IDistributedCache cache, string cacheKey, string requestHash)
    {
        _inner = inner;
        _cache = cache;
        _cacheKey = cacheKey;
        _requestHash = requestHash;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var originalBody = httpContext.Response.Body;
        using var buffer = new MemoryStream();
        httpContext.Response.Body = buffer;

        try
        {
            await _inner.ExecuteAsync(httpContext);
        }
        finally
        {
            httpContext.Response.Body = originalBody;
        }

        var bodyBytes = buffer.ToArray();
        if (bodyBytes.Length > 0)
            await originalBody.WriteAsync(bodyBytes, httpContext.RequestAborted);

        var status = httpContext.Response.StatusCode;
        if (status is >= 200 and < 500 and not 408 and not 429)
        {
            var entry = new IdempotencyCacheEntry(
                _requestHash,
                status,
                httpContext.Response.ContentType,
                Convert.ToBase64String(bodyBytes));

            await _cache.SetStringAsync(
                _cacheKey,
                JsonSerializer.Serialize(entry),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                httpContext.RequestAborted);
        }
    }
}
