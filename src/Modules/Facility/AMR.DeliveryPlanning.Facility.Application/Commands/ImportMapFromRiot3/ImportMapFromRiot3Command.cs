using AMR.DeliveryPlanning.Facility.Domain.Entities;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Facility.Application.Commands.ImportMapFromRiot3;

public record ImportMapFromRiot3Command(int Riot3MapId) : ICommand<ImportMapFromRiot3Result>;

public record ImportMapFromRiot3Result(
    Guid MapId,
    string MapName,
    int StationsImported,
    IReadOnlyList<ImportedStationDto> Stations);

public record ImportedStationDto(
    Guid StationId,
    string Name,
    int Riot3StationId,
    StationType Type);
