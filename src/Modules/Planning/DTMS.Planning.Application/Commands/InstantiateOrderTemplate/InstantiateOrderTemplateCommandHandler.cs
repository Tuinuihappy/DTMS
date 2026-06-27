using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Repositories;
using DTMS.SharedKernel.Exceptions;
using DTMS.SharedKernel.Messaging;

namespace AMR.DeliveryPlanning.Planning.Application.Commands.InstantiateOrderTemplate;

internal sealed class InstantiateOrderTemplateCommandHandler
    : ICommandHandler<InstantiateOrderTemplateCommand, InstantiateOrderTemplateResult>
{
    private readonly IOrderTemplateRepository _templateRepo;
    private readonly IOrderTemplateResolver _resolver;
    private readonly IRobotOrderDispatcher _dispatcher;

    public InstantiateOrderTemplateCommandHandler(
        IOrderTemplateRepository templateRepo,
        IOrderTemplateResolver resolver,
        IRobotOrderDispatcher dispatcher)
    {
        _templateRepo = templateRepo;
        _resolver = resolver;
        _dispatcher = dispatcher;
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

        // UpperKey is the correlation id we hand to RIOT3 so its callbacks
        // can be matched back to this dispatch. Default to a fresh Guid when
        // the caller didn't supply one.
        var upperKey = string.IsNullOrWhiteSpace(request.UpperKey)
            ? Guid.NewGuid().ToString()
            : request.UpperKey;

        if (request.DryRun)
        {
            return Result<InstantiateOrderTemplateResult>.Success(
                new InstantiateOrderTemplateResult(upperKey, Riot3OrderKey: null, ResolvedOrder: resolved, DryRun: true));
        }

        var sendResult = await _dispatcher.SendAsync(upperKey, resolved, cancellationToken);
        if (!sendResult.IsSuccess)
        {
            return Result<InstantiateOrderTemplateResult>.Failure(sendResult.Error!);
        }

        return Result<InstantiateOrderTemplateResult>.Success(
            new InstantiateOrderTemplateResult(upperKey, sendResult.Value!.VendorOrderKey, resolved, DryRun: false));
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
