using AMR.DeliveryPlanning.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Dispatch.Application.Services;

/// <summary>
/// Vendor-agnostic surface for envelope-level lifecycle operations
/// (cancel / pause / resume) against the RIOT3 upperKey. Implemented at
/// the composition root by a vendor-specific adapter so this assembly
/// stays free of RIOT3 references.
/// </summary>
public interface IVendorEnvelopeOperationService
{
    Task<Result> CancelAsync(string upperKey, CancellationToken cancellationToken = default);
    Task<Result> PauseAsync(string upperKey, CancellationToken cancellationToken = default);
    Task<Result> ResumeAsync(string upperKey, CancellationToken cancellationToken = default);
}
