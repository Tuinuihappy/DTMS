using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Abstractions.Models;

namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

public interface IVehicleCommandService
{
    Task<Result> SendTaskAsync(Guid vehicleId, RobotTaskCommand command, CancellationToken cancellationToken = default);
    Task<Result> CancelTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default);
    Task<Result> PauseTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default);
    Task<Result> ResumeTaskAsync(Guid vehicleId, Guid taskId, CancellationToken cancellationToken = default);
}
