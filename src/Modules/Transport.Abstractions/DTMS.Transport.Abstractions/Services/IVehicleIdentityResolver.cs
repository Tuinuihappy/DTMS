namespace DTMS.Transport.Abstractions.Services;

public interface IVehicleIdentityResolver
{
    Task<Guid?> ResolveVehicleIdAsync(string adapterKey, string vendorVehicleKey, CancellationToken cancellationToken = default);
}
