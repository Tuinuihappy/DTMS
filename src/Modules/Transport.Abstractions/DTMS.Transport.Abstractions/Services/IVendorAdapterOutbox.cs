using DTMS.SharedKernel.Domain;

namespace DTMS.Transport.Abstractions.Services;

public interface IVendorAdapterOutbox
{
    Task AddAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
