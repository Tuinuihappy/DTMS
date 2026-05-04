using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.VendorAdapter.Abstractions.Services;

public interface IVendorAdapterOutbox
{
    Task AddAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IIntegrationEvent;

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
