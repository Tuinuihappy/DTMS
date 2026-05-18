namespace AMR.DeliveryPlanning.Facility.Application.Services;

public interface IRiot3FacilityClient
{
    Task<Riot3MapInfo?> GetMapAsync(int riot3MapId, CancellationToken ct = default);
    Task<List<Riot3StationInfo>> GetStationsAsync(int riot3MapId, CancellationToken ct = default);
}

public record Riot3MapInfo(int Id, string MapName);
public record Riot3StationInfo(int Id, string Name, double PosX, double PosY, double PosYaw);
