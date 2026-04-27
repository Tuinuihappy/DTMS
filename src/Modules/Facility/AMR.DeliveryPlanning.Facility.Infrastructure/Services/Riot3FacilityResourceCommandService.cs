using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AMR.DeliveryPlanning.Facility.Domain.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

public class Riot3FacilityResourceCommandService : IFacilityResourceCommandService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<Riot3FacilityResourceCommandService> _logger;

    public Riot3FacilityResourceCommandService(HttpClient httpClient, ILogger<Riot3FacilityResourceCommandService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> SendCommandAsync(string resourceType, string vendorRef, string command, CancellationToken cancellationToken = default)
    {
        // Map our canonical resource type to RIOT3.0 deviceType enum
        var deviceType = resourceType switch
        {
            "Door" => "DOOR",
            "AirShowerDoor" => "AIR_SHOWER_DOOR",
            "Elevator" => "ELEVATOR",
            "Charger" => "CHARGER",
            _ => "COMMON"
        };

        var request = new { deviceKey = vendorRef, command };

        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/v4/device/{deviceType}/command", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Sent {Command} to {DeviceType} {VendorRef}", command, deviceType, vendorRef);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send command to {DeviceType} {VendorRef}", deviceType, vendorRef);
            return false;
        }
    }
}
