using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.CreateCrossDockJobs;

/// <summary>
/// Creates linked inbound + outbound Cross-Dock jobs.
/// Inbound drops at dockStation; Outbound picks from dockStation after handling dwell.
/// </summary>
public record CreateCrossDockJobsCommand(
    Guid InboundOrderId,
    Guid OutboundOrderId,
    Guid InboundPickupStationId,
    Guid DockStationId,
    Guid OutboundDropStationId,
    int HandlingDwellMinutes = 5,
    string Priority = "Normal"
) : ICommand<CrossDockResult>;

public record CrossDockResult(Guid InboundJobId, Guid OutboundJobId, Guid DependencyId);
