using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Commands.ResumeTrip;

public record ResumeTripCommand(Guid TripId) : ICommand;
