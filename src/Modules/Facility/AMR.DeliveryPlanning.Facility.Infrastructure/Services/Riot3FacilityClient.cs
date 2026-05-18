using System.Net.Http.Json;
using AMR.DeliveryPlanning.Facility.Application.Services;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Facility.Infrastructure.Services;

public sealed class Riot3FacilityClient : IRiot3FacilityClient
{
    private readonly HttpClient _http;
    private readonly ILogger<Riot3FacilityClient> _logger;

    public Riot3FacilityClient(HttpClient http, ILogger<Riot3FacilityClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Riot3MapInfo?> GetMapAsync(int riot3MapId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<Riot3MapListResponse>(
                "/api/v4/maps?pageSize=-1", ct);

            var record = response?.Data?.Records.FirstOrDefault(m => m.Id == riot3MapId);
            return record is null ? null : new Riot3MapInfo(record.Id, record.MapName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch map {MapId} from RIOT3", riot3MapId);
            return null;
        }
    }

    public async Task<List<Riot3StationInfo>> GetStationsAsync(int riot3MapId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<Riot3StationListResponse>(
                $"/api/v4/map/file/{riot3MapId}/stations", ct);

            return response?.Data?
                .Select(s => new Riot3StationInfo(s.Id, s.Name, s.PosX, s.PosY, s.PosYaw))
                .ToList() ?? new List<Riot3StationInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch stations for map {MapId} from RIOT3", riot3MapId);
            return new List<Riot3StationInfo>();
        }
    }
}
