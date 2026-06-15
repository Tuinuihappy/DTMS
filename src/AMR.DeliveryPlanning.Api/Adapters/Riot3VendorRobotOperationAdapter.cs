using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Models;
using AMR.DeliveryPlanning.VendorAdapter.Riot3.Services;

namespace AMR.DeliveryPlanning.Api.Adapters;

// Composition-root bridge: forwards Dispatch.Application's vendor-agnostic
// robot operations to the concrete RIOT3 client. Lives in the API project
// so Dispatch.Application has no compile-time link to RIOT3 — mirrors
// the Riot3VendorEnvelopeOperationAdapter pattern.
internal sealed class Riot3VendorRobotOperationAdapter : IVendorRobotOperationService
{
    private readonly Riot3CommandService _riot3;

    public Riot3VendorRobotOperationAdapter(Riot3CommandService riot3)
    {
        _riot3 = riot3;
    }

    public async Task<Result<VendorOperationOutcome>> PassAsync(string vendorVehicleKey, CancellationToken cancellationToken = default)
    {
        var result = await _riot3.PassRobotAsync(vendorVehicleKey, cancellationToken);
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
