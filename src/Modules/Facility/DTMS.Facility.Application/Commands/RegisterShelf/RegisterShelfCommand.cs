using DTMS.SharedKernel.Messaging;

namespace DTMS.Facility.Application.Commands.RegisterShelf;

public record RegisterShelfCommand(
    Guid MapId,
    string Rfid,
    double MaxWeightKg,
    int MaxSlots) : ICommand<Guid>;
