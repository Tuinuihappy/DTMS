using DTMS.SharedKernel.Domain;

namespace DTMS.Fleet.Application.Services;

public interface IFleetOutbox
{
    Task AddAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
