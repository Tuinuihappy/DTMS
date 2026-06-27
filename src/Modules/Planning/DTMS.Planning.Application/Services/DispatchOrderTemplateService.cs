using DTMS.Dispatch.Application.Commands.CreateEnvelopeTrip;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AMR.DeliveryPlanning.Planning.Application.Services;

public sealed class DispatchOrderTemplateService : IDispatchOrderTemplateService
{
    private readonly IOrderTemplateRepository _templateRepository;
    private readonly IOrderTemplateResolver _resolver;
    private readonly IRobotOrderDispatcher _dispatcher;
    private readonly ISender _sender;
    private readonly ILogger<DispatchOrderTemplateService> _logger;

    public DispatchOrderTemplateService(
        IOrderTemplateRepository templateRepository,
        IOrderTemplateResolver resolver,
        IRobotOrderDispatcher dispatcher,
        ISender sender,
        ILogger<DispatchOrderTemplateService> logger)
    {
        _templateRepository = templateRepository;
        _resolver = resolver;
        _dispatcher = dispatcher;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<DispatchTemplateResult>> DispatchByRouteAsync(
        Guid deliveryOrderId,
        Guid pickupStationId,
        Guid dropStationId,
        string upperKey,
        int attemptNumber = 1,
        Guid? previousAttemptId = null,
        int? priorityOverride = null,
        string? appointVehicleKeyOverride = null,
        string? appointVehicleNameOverride = null,
        string? appointVehicleGroupKeyOverride = null,
        string? appointVehicleGroupNameOverride = null,
        string? appointQueueWaitAreaOverride = null,
        Guid? jobId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(upperKey))
            return Result<DispatchTemplateResult>.Failure("UpperKey is required for envelope dispatch.");

        var template = await _templateRepository.FindByRouteAsync(pickupStationId, dropStationId, cancellationToken);
        if (template is null)
        {
            _logger.LogInformation(
                "[EnvelopeDispatch] No OrderTemplate registered for route {Pickup} → {Drop}; caller should fall back.",
                pickupStationId, dropStationId);
            return Result<DispatchTemplateResult>.Failure(
                $"No active OrderTemplate registered for route {pickupStationId} → {dropStationId}.");
        }

        ResolvedOrder resolved;
        try
        {
            resolved = await _resolver.ResolveAsync(template, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("[EnvelopeDispatch] Template {TemplateId} ({TemplateName}) resolution failed: {Error}",
                template.Id, template.Name, ex.Message);
            return Result<DispatchTemplateResult>.Failure(ex.Message);
        }

        // Apply per-dispatch overrides on top of the resolved template.
        // Empty/whitespace strings fall through to the template's stored value
        // so callers can send "" without wiping out the configured default.
        resolved = resolved with
        {
            Priority = priorityOverride ?? resolved.Priority,
            AppointVehicleKey = NotBlank(appointVehicleKeyOverride) ?? resolved.AppointVehicleKey,
            AppointVehicleName = NotBlank(appointVehicleNameOverride) ?? resolved.AppointVehicleName,
            AppointVehicleGroupKey = NotBlank(appointVehicleGroupKeyOverride) ?? resolved.AppointVehicleGroupKey,
            AppointVehicleGroupName = NotBlank(appointVehicleGroupNameOverride) ?? resolved.AppointVehicleGroupName,
            AppointQueueWaitArea = NotBlank(appointQueueWaitAreaOverride) ?? resolved.AppointQueueWaitArea
        };

        var sendResult = await _dispatcher.SendAsync(upperKey, resolved, cancellationToken);
        if (!sendResult.IsSuccess)
        {
            _logger.LogWarning("[EnvelopeDispatch] Vendor rejected envelope for template {TemplateName} (upperKey {UpperKey}): {Error}",
                template.Name, upperKey, sendResult.Error);
            return Result<DispatchTemplateResult>.Failure(sendResult.Error!);
        }

        var vendorOrderKey = sendResult.Value!.VendorOrderKey;
        var requestJson = sendResult.Value!.RequestJson;
        _logger.LogInformation("[EnvelopeDispatch] ✓ Dispatched {TemplateName} via envelope (upperKey {UpperKey} → vendorOrderKey {OrderKey})",
            template.Name, upperKey, vendorOrderKey);

        // Persist a Trip row so the webhook handler has a correlation target.
        // Cross-module via MediatR — Dispatch.Application owns the handler.
        // Fall back to empty TripId if the create fails (vendor side already
        // accepted the envelope, so we can't roll back the dispatch).
        // Route context + retry lineage are persisted on the Trip so a
        // future retry can re-resolve the template without re-reading the
        // delivery order.
        // Snapshot data — template name, priority, and the exact request
        // JSON DTMS sent — go into Trip.* fields for compliance / detail UI.
        var tripCreate = await _sender.Send(
            new CreateEnvelopeTripCommand(
                deliveryOrderId,
                upperKey,
                vendorOrderKey,
                pickupStationId,
                dropStationId,
                attemptNumber,
                previousAttemptId,
                TemplateNameAtDispatch: template.Name,
                PriorityAtDispatch: resolved.Priority,
                VendorRequestSnapshot: requestJson,
                JobId: jobId),
            cancellationToken);
        var tripId = tripCreate.IsSuccess ? tripCreate.Value : Guid.Empty;
        if (!tripCreate.IsSuccess)
        {
            _logger.LogError(
                "[EnvelopeDispatch] Vendor accepted envelope but Trip persistence failed for UpperKey {UpperKey}: {Error}",
                upperKey, tripCreate.Error);
        }
        else
        {
            // Bind the order's items at this (pickup, drop) pair to the new
            // Trip so the trip's terminal webhook updates only THIS trip's
            // items — multi-group orders no longer finalize off the first
            // completed trip. The consumer also falls back to (pickup, drop)
            // matching if this binding fails, so we log and proceed.
            var assignResult = await _sender.Send(
                new DTMS.DeliveryOrder.Application.Commands.AssignItemsToTrip.AssignItemsToTripCommand(
                    deliveryOrderId, tripId, attemptNumber, pickupStationId, dropStationId),
                cancellationToken);
            if (!assignResult.IsSuccess)
            {
                _logger.LogWarning(
                    "[EnvelopeDispatch] Item binding failed for Trip {TripId} on Order {OrderId} ({UpperKey}): {Error}",
                    tripId, deliveryOrderId, upperKey, assignResult.Error);
            }
        }

        return Result<DispatchTemplateResult>.Success(
            new DispatchTemplateResult(template.Id, template.Name, vendorOrderKey, tripId, resolved));
    }

    private static string? NotBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
