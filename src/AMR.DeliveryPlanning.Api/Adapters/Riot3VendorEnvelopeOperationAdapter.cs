using AMR.DeliveryPlanning.DeliveryOrder.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Composition-root bridge: forwards Dispatch.Application's vendor-agnostic
// envelope operations to the concrete RIOT3 client. Lives in the API
// project so Dispatch.Application has no compile-time link to RIOT3.
//
// Implements IVendorOperationsAdapter (Mode=Amr) so VendorOperationsRouter
// can auto-discover this adapter at startup — no explicit Register call
// needed in composition root.
internal sealed class Riot3VendorEnvelopeOperationAdapter : IVendorEnvelopeOperationService, IVendorOperationsAdapter
{
    private readonly Riot3CommandService _riot3;

    public Riot3VendorEnvelopeOperationAdapter(Riot3CommandService riot3)
    {
        _riot3 = riot3;
    }

    public TransportMode Mode => TransportMode.Amr;

    public async Task<Result<VendorOperationOutcome>> CancelAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
    {
        var result = await _riot3.CancelEnvelopeAsync(vendorOrderKey, cancellationToken);
        return MapResult(result);
    }

    public async Task<Result<VendorOperationOutcome>> PauseAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
    {
        var result = await _riot3.PauseEnvelopeAsync(vendorOrderKey, cancellationToken);
        return MapResult(result);
    }

    public async Task<Result<VendorOperationOutcome>> ResumeAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
    {
        var result = await _riot3.ResumeEnvelopeAsync(vendorOrderKey, cancellationToken);
        return MapResult(result);
    }

    public async Task<Result<VendorOperationOutcome>> ResumeFromHangAsync(string vendorOrderKey, CancellationToken cancellationToken = default)
    {
        var result = await _riot3.ResumeFromHangEnvelopeAsync(vendorOrderKey, cancellationToken);
        return MapResult(result);
    }

    private static Result<VendorOperationOutcome> MapResult(Result<Riot3OperationOutcome> source)
    {
        if (source.IsFailure)
            return Result<VendorOperationOutcome>.Failure(source.Error!);

        var mapped = source.Value switch
        {
            Riot3OperationOutcome.Accepted       => VendorOperationOutcome.Accepted,
            Riot3OperationOutcome.NoVendorRecord => VendorOperationOutcome.NoVendorRecord,
            _ => VendorOperationOutcome.Rejected
        };
        return Result<VendorOperationOutcome>.Success(mapped);
    }
}
