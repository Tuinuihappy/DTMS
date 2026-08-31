using DTMS.Fleet.Domain.Entities;

namespace DTMS.Fleet.Domain.Repositories;

public interface ICarrierMaintenanceLogRepository
{
    /// <summary>The episode still open for this carrier, or null. At most one can
    /// exist — a partial unique index enforces that, not this lookup.</summary>
    Task<CarrierMaintenanceLog?> GetOpenForCarrierAsync(Guid carrierId, CancellationToken ct = default);

    /// <summary>How much history deleting this carrier would destroy. Feeds
    /// <see cref="Carrier.CanDelete"/>, which refuses when the answer is not zero.</summary>
    Task<int> CountForCarrierAsync(Guid carrierId, CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<List<CarrierMaintenanceLog>> GetHistoryAsync(Guid carrierId, CancellationToken ct = default);

    Task AddAsync(CarrierMaintenanceLog log, CancellationToken ct = default);
}
