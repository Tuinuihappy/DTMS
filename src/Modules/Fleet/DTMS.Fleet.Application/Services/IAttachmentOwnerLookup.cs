using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Repositories;

namespace DTMS.Fleet.Application.Services;

/// <summary>
/// Answers "does this owner exist" for each kind an attachment can hang off.
///
/// <para>Without it every handler would inject three repositories and repeat
/// the same switch. It also keeps the check ahead of the insert, so a bad owner
/// id comes back as a plain message instead of a foreign-key violation
/// surfacing as a 409 about a constraint name.</para>
/// </summary>
public interface IAttachmentOwnerLookup
{
    Task<bool> ExistsAsync(AttachmentOwner owner, Guid ownerId, CancellationToken ct = default);
}

// Public, unlike the handlers in this assembly: the composition root registers
// it by name and lives outside.
public sealed class AttachmentOwnerLookup : IAttachmentOwnerLookup
{
    private readonly ICarrierRepository _carriers;
    private readonly ICarrierTypeRepository _carrierTypes;
    private readonly ICarrierMaintenanceLogRepository _logs;

    public AttachmentOwnerLookup(
        ICarrierRepository carriers,
        ICarrierTypeRepository carrierTypes,
        ICarrierMaintenanceLogRepository logs)
    {
        _carriers = carriers;
        _carrierTypes = carrierTypes;
        _logs = logs;
    }

    public async Task<bool> ExistsAsync(
        AttachmentOwner owner, Guid ownerId, CancellationToken ct = default) => owner switch
    {
        AttachmentOwner.Carrier => await _carriers.GetByIdAsync(ownerId, ct) is not null,
        AttachmentOwner.CarrierType => await _carrierTypes.GetByIdAsync(ownerId, ct) is not null,
        AttachmentOwner.MaintenanceLog => await _logs.ExistsAsync(ownerId, ct),
        _ => false
    };
}
