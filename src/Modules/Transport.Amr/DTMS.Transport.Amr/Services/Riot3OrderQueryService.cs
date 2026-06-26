using System.Net;
using System.Net.Http.Json;
using DTMS.Transport.Amr.Models;
using Microsoft.Extensions.Logging;

namespace DTMS.Transport.Amr.Services;

public sealed class Riot3OrderQueryService : IRiot3OrderQueryService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Riot3OrderQueryService> _logger;

    public Riot3OrderQueryService(HttpClient httpClient, ILogger<Riot3OrderQueryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Riot3OrderQueryData?> GetOrderByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v4/orders/{Uri.EscapeDataString(upperKey)}?isUpper=true";
        try
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("RIOT3 GET {Path} returned 404 (upperKey not found)", path);
                return null;
            }
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<Riot3OrderQueryResponse>(cancellationToken);
            if (payload?.Code != "0")
            {
                // RIOT3 returns HTTP 200 + code "E110014" ("订单为空" / order is
                // empty) when the upperKey doesn't exist on the vendor side —
                // treat as "no record" so the reconciler doesn't keep retrying.
                _logger.LogDebug("RIOT3 GET {Path} returned non-success code {Code} ({Message}) — treating as no record",
                    path, payload?.Code ?? "(null)", payload?.Message ?? "(no message)");
                return null;
            }
            return payload.Data;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RIOT3 GET {Path} failed", path);
            throw;
        }
    }

    public async Task<string?> GetRawByUpperKeyAsync(string upperKey, CancellationToken cancellationToken = default)
    {
        var path = $"/api/v4/orders/{Uri.EscapeDataString(upperKey)}?isUpper=true";
        try
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("RIOT3 GET {Path} returned 404 (no raw payload to capture)", path);
                return null;
            }
            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            // Quick guard against the E110014 "order is empty" case — don't
            // pollute Trip.VendorFinalSnapshot with a not-found stub.
            if (raw.Contains("\"E110014\"", StringComparison.Ordinal))
            {
                _logger.LogDebug("RIOT3 GET {Path} returned E110014 — treating as no record", path);
                return null;
            }
            return raw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RIOT3 GET {Path} (raw) failed", path);
            throw;
        }
    }
}
