using DTMS.Transport.Abstractions.Models;

namespace DTMS.Transport.Abstractions.Services;

public interface IActionCatalogService
{
    Task<ActionCatalogEntry?> GetAsync(string vehicleTypeKey, string canonicalAction, CancellationToken cancellationToken = default);
    Task<List<ActionCatalogEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ActionCatalogEntry entry, CancellationToken cancellationToken = default);
}
