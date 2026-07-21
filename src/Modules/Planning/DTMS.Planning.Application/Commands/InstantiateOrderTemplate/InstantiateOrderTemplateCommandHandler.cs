using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Planning.Application.Commands.InstantiateOrderTemplate;

// Public (not internal) to match the other unit-tested handlers in this
// module — the de-dup decisions here are the whole point of the feature and
// need direct test coverage.
public sealed class InstantiateOrderTemplateCommandHandler
    : ICommandHandler<InstantiateOrderTemplateCommand, InstantiateOrderTemplateResult>
{
    private readonly IOrderTemplateRepository _templateRepo;
    private readonly IDispatchClaimRepository _claimRepo;
    private readonly IOrderTemplateResolver _resolver;
    private readonly IRobotOrderDispatcher _dispatcher;
    private readonly IRobotOrderStatusQuery _statusQuery;

    public InstantiateOrderTemplateCommandHandler(
        IOrderTemplateRepository templateRepo,
        IDispatchClaimRepository claimRepo,
        IOrderTemplateResolver resolver,
        IRobotOrderDispatcher dispatcher,
        IRobotOrderStatusQuery statusQuery)
    {
        _templateRepo = templateRepo;
        _claimRepo = claimRepo;
        _resolver = resolver;
        _dispatcher = dispatcher;
        _statusQuery = statusQuery;
    }

    public async Task<Result<InstantiateOrderTemplateResult>> Handle(
        InstantiateOrderTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepo.GetByIdAsync(request.OrderTemplateId, cancellationToken)
            ?? throw new NotFoundException($"OrderTemplate {request.OrderTemplateId} not found.");

        if (!template.IsActive)
            return Result<InstantiateOrderTemplateResult>.Failure(
                $"OrderTemplate '{template.Name}' is inactive; activate it before instantiating.");

        ResolvedOrder resolved;
        try
        {
            resolved = await _resolver.ResolveAsync(template, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // ActionTemplate reference missing — surface the resolver error
            // as a 400 so the caller knows to fix the template, not retry.
            return Result<InstantiateOrderTemplateResult>.Failure(ex.Message);
        }

        // Apply runtime overrides. Empty/whitespace strings collapse to the
        // template's stored value so callers can send "" without wiping out
        // the configured default.
        resolved = ApplyOverrides(resolved, request);

        // Dry run never touches the vendor and never consumes a claim, so it
        // needs no idempotency key at all.
        if (request.DryRun)
        {
            var previewKey = string.IsNullOrWhiteSpace(request.UpperKey)
                ? Guid.NewGuid().ToString()
                : request.UpperKey;
            return Result<InstantiateOrderTemplateResult>.Success(
                new InstantiateOrderTemplateResult(previewKey, Riot3OrderKey: null, resolved, DryRun: true));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Result<InstantiateOrderTemplateResult>.Failure(
                "Idempotency-Key is required for dispatch.");

        var requestHash = ComputeRequestHash(request);

        // Claim first, dispatch second. The unique index arbitrates between
        // concurrent callers, and the committed row means a crash mid-dispatch
        // still leaves a trace we can reason about later.
        var claim = await _claimRepo.TryClaimAsync(
            request.IdempotencyKey, template.Id, requestHash, cancellationToken);

        if (claim is null)
        {
            var existing = await _claimRepo.GetByKeyAsync(request.IdempotencyKey, cancellationToken);
            if (existing is null)
            {
                // Claim vanished between the failed insert and this read
                // (manual delete). Treat as retryable rather than guessing.
                return Result<InstantiateOrderTemplateResult>.Failure(
                    "Could not establish dispatch claim; please retry.");
            }

            return await HandleExistingClaimAsync(existing, requestHash, resolved, cancellationToken);
        }

        return await DispatchAndRecordAsync(claim, resolved, cancellationToken);
    }

    // Someone already used this key. Decide replay / reject / retry without
    // ever re-sending blindly — the vendor does not de-duplicate, so a wrong
    // "just retry" here means a second robot really moves.
    private async Task<Result<InstantiateOrderTemplateResult>> HandleExistingClaimAsync(
        DispatchClaim existing,
        string requestHash,
        ResolvedOrder resolved,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            // Same key, different payload — a caller mistake (e.g. priority
            // edited then re-submitted). Replaying the original would silently
            // ignore the edit, so reject instead.
            return Result<InstantiateOrderTemplateResult>.Failure(
                $"{InstantiateFailureCodes.BodyMismatch}: this Idempotency-Key was already used with a different request. Use a new key for a different dispatch.");
        }

        switch (existing.Status)
        {
            case DispatchClaimStatus.Succeeded:
                return Result<InstantiateOrderTemplateResult>.Success(
                    new InstantiateOrderTemplateResult(
                        existing.UpperKey, existing.VendorOrderKey, resolved, DryRun: false, Replayed: true));

            case DispatchClaimStatus.Failed:
                // Known-failed attempt: nothing was created vendor-side, so
                // this key can be re-driven from scratch.
                return await DispatchAndRecordAsync(existing, resolved, cancellationToken);

            default:
                // InProgress — either a genuine concurrent request, or an
                // earlier attempt that timed out. Ask the vendor.
                return await ResolveInDoubtAsync(existing, resolved, cancellationToken);
        }
    }

    // In-doubt: we never received a vendor confirmation for this claim. Ask
    // whether the order exists rather than guessing. Inline only — there is no
    // background sweeper, so a claim nobody retries simply stays InProgress as
    // an audit record.
    private async Task<Result<InstantiateOrderTemplateResult>> ResolveInDoubtAsync(
        DispatchClaim claim,
        ResolvedOrder resolved,
        CancellationToken cancellationToken)
    {
        var presence = await _statusQuery.CheckAsync(claim.UpperKey, cancellationToken);

        switch (presence)
        {
            case RobotOrderPresence.Exists:
                // The earlier attempt did land. Adopt it as the outcome.
                claim.MarkSucceeded(claim.VendorOrderKey);
                await _claimRepo.SaveChangesAsync(cancellationToken);
                return Result<InstantiateOrderTemplateResult>.Success(
                    new InstantiateOrderTemplateResult(
                        claim.UpperKey, claim.VendorOrderKey, resolved, DryRun: false, Replayed: true));

            case RobotOrderPresence.NotFound:
                // Vendor is certain nothing was created — safe to drive again.
                return await DispatchAndRecordAsync(claim, resolved, cancellationToken);

            default:
                // Unknown: we could not ask. Blocking briefly costs an operator
                // a retry; guessing "retry" costs a duplicate robot movement.
                // Stay conservative and let a human decide.
                return Result<InstantiateOrderTemplateResult>.Failure(
                    $"{InstantiateFailureCodes.InProgress}: a dispatch with this key is still being confirmed. Please wait a moment and check the latest dispatch before sending again.");
        }
    }

    private async Task<Result<InstantiateOrderTemplateResult>> DispatchAndRecordAsync(
        DispatchClaim claim,
        ResolvedOrder resolved,
        CancellationToken cancellationToken)
    {
        try
        {
            var sendResult = await _dispatcher.SendAsync(claim.UpperKey, resolved, cancellationToken);
            if (!sendResult.IsSuccess)
            {
                // Vendor answered and refused — nothing was created, so mark it
                // failed and let the same key be retried after a fix.
                claim.MarkFailed(sendResult.Error);
                await _claimRepo.SaveChangesAsync(cancellationToken);
                return Result<InstantiateOrderTemplateResult>.Failure(sendResult.Error!);
            }

            claim.MarkSucceeded(sendResult.Value!.VendorOrderKey);
            await _claimRepo.SaveChangesAsync(cancellationToken);

            return Result<InstantiateOrderTemplateResult>.Success(
                new InstantiateOrderTemplateResult(
                    claim.UpperKey, sendResult.Value!.VendorOrderKey, resolved, DryRun: false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No answer from the vendor: the order may or may not exist. Leave
            // the claim InProgress on purpose — marking it Failed would invite
            // a retry that could duplicate a robot order.
            return Result<InstantiateOrderTemplateResult>.Failure(
                $"{InstantiateFailureCodes.InProgress}: dispatch result unconfirmed ({ex.Message}). Check the latest dispatch before sending again.");
        }
    }

    // Covers every field that changes what the robot is asked to do, so a
    // reused key with edited overrides is caught instead of silently replaying
    // the original request.
    private static string ComputeRequestHash(InstantiateOrderTemplateCommand request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.OrderTemplateId,
            request.PriorityOverride,
            request.AppointVehicleKeyOverride,
            request.AppointVehicleNameOverride,
            request.AppointVehicleGroupKeyOverride,
            request.AppointVehicleGroupNameOverride,
            request.AppointQueueWaitAreaOverride
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..64].ToLowerInvariant();
    }

    private static ResolvedOrder ApplyOverrides(ResolvedOrder resolved, InstantiateOrderTemplateCommand request)
    {
        return resolved with
        {
            Priority = request.PriorityOverride ?? resolved.Priority,
            AppointVehicleKey = NotBlank(request.AppointVehicleKeyOverride) ?? resolved.AppointVehicleKey,
            AppointVehicleName = NotBlank(request.AppointVehicleNameOverride) ?? resolved.AppointVehicleName,
            AppointVehicleGroupKey = NotBlank(request.AppointVehicleGroupKeyOverride) ?? resolved.AppointVehicleGroupKey,
            AppointVehicleGroupName = NotBlank(request.AppointVehicleGroupNameOverride) ?? resolved.AppointVehicleGroupName,
            AppointQueueWaitArea = NotBlank(request.AppointQueueWaitAreaOverride) ?? resolved.AppointQueueWaitArea
        };
    }

    private static string? NotBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
