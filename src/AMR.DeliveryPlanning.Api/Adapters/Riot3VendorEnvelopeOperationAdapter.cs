using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Composition-root bridge: forwards Dispatch.Application's vendor-agnostic
// envelope operations to the concrete RIOT3 client. Lives in the API
// project so Dispatch.Application has no compile-time link to RIOT3.
internal sealed class Riot3VendorEnvelopeOperationAdapter : IVendorEnvelopeOperationService
{
    private readonly Riot3CommandService _riot3;

    public Riot3VendorEnvelopeOperationAdapter(Riot3CommandService riot3)
    {
        _riot3 = riot3;
    }

    public Task<Result> CancelAsync(string upperKey, CancellationToken cancellationToken = default)
        => _riot3.CancelEnvelopeAsync(upperKey, cancellationToken);

    public Task<Result> PauseAsync(string upperKey, CancellationToken cancellationToken = default)
        => _riot3.PauseEnvelopeAsync(upperKey, cancellationToken);

    public Task<Result> ResumeAsync(string upperKey, CancellationToken cancellationToken = default)
        => _riot3.ResumeEnvelopeAsync(upperKey, cancellationToken);
}
