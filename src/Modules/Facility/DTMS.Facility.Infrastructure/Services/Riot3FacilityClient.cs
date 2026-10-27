using System.Net.Http.Json;
using DTMS.Facility.Application.Services;

namespace DTMS.Facility.Infrastructure.Services;

public sealed class Riot3FacilityClient : IRiot3FacilityClient
{
    private readonly HttpClient _http;

    public Riot3FacilityClient(HttpClient http)
    {
        _http = http;
    }

    // Transport / server errors propagate. Returning null is reserved for the case
    // where the API call succeeded but the requested mapId isn't in RIOT3's records
    // — callers must distinguish "RIOT3 unreachable" from "map doesn't exist".
    public async Task<Riot3MapInfo?> GetMapAsync(int riot3MapId, CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<Riot3MapListResponse>(
            "/api/v4/maps?pageSize=-1", ct);

        EnsureSuccess(response?.Code, response?.Message, "map list");
        if (response!.Data is null)
            throw new InvalidOperationException("RIOT3 map list returned no data.");

        var record = response.Data.Records.FirstOrDefault(m => m.Id == riot3MapId);
        return record is null ? null : new Riot3MapInfo(record.Id, record.MapName);
    }

    // Same contract: throws on failure so callers don't mistake "RIOT3 down"
    // for "this map legitimately has no stations" and soft-delete everything.
    // RIOT3 reports errors as HTTP 200 + a non-"0" code with no data (e.g.
    // E800075 "地图不存在" for an unknown map) — before this check that
    // parsed as an empty list and the sync deactivated every station.
    public async Task<List<Riot3StationInfo>> GetStationsAsync(int riot3MapId, CancellationToken ct = default)
    {
        var response = await _http.GetFromJsonAsync<Riot3StationListResponse>(
            $"/api/v4/map/file/{riot3MapId}/stations", ct);

        EnsureSuccess(response?.Code, response?.Message, $"stations of map {riot3MapId}");
        if (response!.Data is null)
            throw new InvalidOperationException($"RIOT3 returned no station data for map {riot3MapId}.");

        return response.Data
            .Select(s => new Riot3StationInfo(s.Id, s.Name, s.PosX, s.PosY, s.PosYaw))
            .ToList();
    }

    private static void EnsureSuccess(string? code, string? message, string what)
    {
        if (code != "0")
            throw new InvalidOperationException(
                $"RIOT3 rejected the {what} request: {code ?? "(no response)"} {message}".TrimEnd());
    }
}
