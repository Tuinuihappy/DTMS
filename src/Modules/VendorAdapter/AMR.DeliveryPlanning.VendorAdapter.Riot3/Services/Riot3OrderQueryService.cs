using System.Net;
using System.Net.Http.Json;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

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
            return payload?.Data;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RIOT3 GET {Path} failed", path);
            throw;
        }
    }
}
