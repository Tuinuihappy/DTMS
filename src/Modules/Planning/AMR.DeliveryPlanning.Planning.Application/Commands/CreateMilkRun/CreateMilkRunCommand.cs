using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateMilkRun;

public record MilkRunStopDto(Guid StationId, int SequenceOrder, int? ArrivalOffsetMinutes, int DwellMinutes);

public record CreateMilkRunCommand(
    string TemplateName,
    string CronSchedule,
    List<MilkRunStopDto> Stops,
    string Priority = "Normal"
) : ICommand<Guid>;
