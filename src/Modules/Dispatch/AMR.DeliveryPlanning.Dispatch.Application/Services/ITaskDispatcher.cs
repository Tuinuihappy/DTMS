using AMR.DeliveryPlanning.Dispatch.Domain.Entities;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

public interface ITaskDispatcher
{
    Task DispatchAsync(Guid? vehicleId, RobotTask task, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid? vehicleId, Guid taskId, CancellationToken cancellationToken = default);
    Task PauseAsync(Guid? vehicleId, Guid taskId, CancellationToken cancellationToken = default);
    Task ResumeAsync(Guid? vehicleId, Guid taskId, CancellationToken cancellationToken = default);
}
