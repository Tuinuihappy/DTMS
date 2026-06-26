using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

public interface IRiot3RouteClient
{
    Task<Riot3RouteCostResponse?> GetRouteCostsAsync(
        string mapVendorRef, string stationVendorRef, CancellationToken ct = default);
}

public sealed class Riot3RouteClient : IRiot3RouteClient
{
    private readonly HttpClient _http;
    private readonly ILogger<Riot3RouteClient> _logger;

    public Riot3RouteClient(HttpClient http, ILogger<Riot3RouteClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Riot3RouteCostResponse?> GetRouteCostsAsync(
        string mapVendorRef, string stationVendorRef, CancellationToken ct = default)
    {
        var url = $"/api/v4/route/costs/{Uri.EscapeDataString(mapVendorRef)}/{Uri.EscapeDataString(stationVendorRef)}";
        try
        {
            var response = await _http.GetFromJsonAsync<Riot3RouteCostResponse>(url, ct);
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RIOT3 route cost request failed for map={Map} station={Station}", mapVendorRef, stationVendorRef);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error fetching route costs for map={Map} station={Station}", mapVendorRef, stationVendorRef);
            return null;
        }
    }
}
