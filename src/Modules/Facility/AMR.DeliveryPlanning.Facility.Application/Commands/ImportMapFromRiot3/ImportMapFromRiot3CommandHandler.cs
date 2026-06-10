using AMR.DeliveryPlanning.Facility.Application.Services;
using AMR.DeliveryPlanning.Facility.Domain.Entities;
using AMR.DeliveryPlanning.Facility.Domain.Repositories;
using AMR.DeliveryPlanning.Facility.Domain.ValueObjects;
using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.ImportMapFromRiot3;

internal sealed class ImportMapFromRiot3CommandHandler
    : ICommandHandler<ImportMapFromRiot3Command, ImportMapFromRiot3Result>
{
    private readonly IRiot3FacilityClient _riot3;
    private readonly IMapRepository _mapRepository;

    public ImportMapFromRiot3CommandHandler(
        IRiot3FacilityClient riot3,
        IMapRepository mapRepository)
    {
        _riot3 = riot3;
        _mapRepository = mapRepository;
    }

    public async Task<Result<ImportMapFromRiot3Result>> Handle(
        ImportMapFromRiot3Command request, CancellationToken cancellationToken)
    {
        // 1. Guard: prevent duplicate import
        var existing = await _mapRepository.GetByVendorRefAsync(request.Riot3MapId.ToString(), cancellationToken);
        if (existing is not null)
            return Result<ImportMapFromRiot3Result>.Failure(
                $"Map with RIOT3 id {request.Riot3MapId} already exists (DTMS map id: {existing.Id}).");

        // 2. Fetch map info + stations from RIOT3. Null now means "map doesn't exist"
        // and exceptions mean "RIOT3 unreachable" — surface both as Result.Failure so
        // the caller sees a distinct error instead of a generic 500.
        Riot3MapInfo? riot3Map;
        List<Riot3StationInfo> riot3Stations;
        try
        {
            riot3Map = await _riot3.GetMapAsync(request.Riot3MapId, cancellationToken);
            if (riot3Map is null)
                return Result<ImportMapFromRiot3Result>.Failure(
                    $"Map {request.Riot3MapId} not found in RIOT3.");

            riot3Stations = await _riot3.GetStationsAsync(request.Riot3MapId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return Result<ImportMapFromRiot3Result>.Failure(
                $"RIOT3 unreachable while importing map {request.Riot3MapId}: {ex.Message}");
        }

        // 3. Calculate map bounds from station coordinates (+ 20% padding).
        // Width/Height capture the full extent — peak absolute reach on either
        // axis — so even if stations span negative coords the map declares
        // enough room to render them.
        var maxX = riot3Stations.Any() ? riot3Stations.Max(s => Math.Abs(s.PosX)) * 1.2 : 100_000;
        var maxY = riot3Stations.Any() ? riot3Stations.Max(s => Math.Abs(s.PosY)) * 1.2 : 100_000;

        // 4. Create Map entity
        var map = new Map(
            Guid.NewGuid(),
            riot3Map.MapName,
            version: "imported",
            width: maxX,
            height: maxY,
            mapData: "{}");
        map.SetVendorRef(riot3Map.Id.ToString());

        await _mapRepository.AddAsync(map, cancellationToken);
        await _mapRepository.SaveChangesAsync(cancellationToken);

        // 5. Import stations — coordinates from RIOT3, StationType defaults to Normal
        var imported = new List<ImportedStationDto>();
        foreach (var s in riot3Stations)
        {
            // Raw signed coords so station positions stay aligned with live
            // robot positions (which use the same raw values).
            var coord = new Coordinate(s.PosX, s.PosY, s.PosYaw / 1000.0);
            var station = new Station(Guid.NewGuid(), map.Id, s.Name, coord, StationType.Normal);
            station.SetVendorRef(s.Id.ToString());
            station.SetCode(s.Name);

            map.AddStation(station);
            imported.Add(new ImportedStationDto(station.Id, s.Name, s.Id, StationType.Normal));
        }

        _mapRepository.Update(map);
        await _mapRepository.SaveChangesAsync(cancellationToken);

        return Result<ImportMapFromRiot3Result>.Success(new ImportMapFromRiot3Result(
            map.Id,
            map.Name,
            imported.Count,
            imported));
    }
}
