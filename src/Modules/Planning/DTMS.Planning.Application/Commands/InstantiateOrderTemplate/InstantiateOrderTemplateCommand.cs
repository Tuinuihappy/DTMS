using DTMS.Planning.Application.Services;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.InstantiateOrderTemplate;

// Take a stored OrderTemplate, expand every ActionTemplateName reference
// against the catalog, and POST the result to RIOT3 as a new order.
// Optional fields override the template's defaults at instantiation time
// so the same template can fan out to different vehicles / priorities.
public record InstantiateOrderTemplateCommand(
    Guid OrderTemplateId,
    int? PriorityOverride = null,
    string? AppointVehicleKeyOverride = null,
    string? AppointVehicleNameOverride = null,
    string? AppointVehicleGroupKeyOverride = null,
    string? AppointVehicleGroupNameOverride = null,
    string? AppointQueueWaitAreaOverride = null,
    string? UpperKey = null,           // caller-supplied correlation id; derived from IdempotencyKey when absent
    bool DryRun = false,                // skip the actual RIOT3 call and just return the resolved envelope
    // Identity of the operator's intent, supplied by the caller and stable
    // across retries of that same intent. This is what makes a repeat
    // recognisable — without it the server cannot tell "clicked twice" from
    // "wants a second trip". Required for real dispatch; ignored for DryRun.
    string? IdempotencyKey = null
) : ICommand<InstantiateOrderTemplateResult>;

// Result carries the resolved order (what we would send / did send) plus the
// upperKey we used and the RIOT3 orderKey we got back (null when dryRun=true
// or RIOT3 didn't return one). Replayed=true means this is the stored outcome
// of an earlier identical request, not a fresh dispatch.
public record InstantiateOrderTemplateResult(
    string UpperKey,
    string? Riot3OrderKey,
    ResolvedOrder ResolvedOrder,
    bool DryRun,
    bool Replayed = false);

/// <summary>
/// Failure codes the endpoint maps to distinct HTTP statuses / wire codes so
/// the UI can tell "still working on it" apart from a real error.
/// </summary>
public static class InstantiateFailureCodes
{
    public const string InProgress = "DISPATCH_IN_PROGRESS";
    public const string BodyMismatch = "IDEMPOTENCY_BODY_MISMATCH";
}
