using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Messaging;

namespace DTMS.Fleet.Application.Commands.UpdateCarrierType;

/// <summary>
/// Edits the mutable metadata. <c>Code</c> addresses the row and is how every
/// carrier payload names its type (ADR-019), so it is deliberately not editable
/// — a rename would silently invalidate callers holding the old one, the same
/// way it would on <see cref="Commands.UpdateCarrier.UpdateCarrierCommand"/>.
/// </summary>
public record UpdateCarrierTypeCommand(
    string Code,
    string DisplayName,
    string AmrCapability,
    double? MaxWeightKg = null,
    int? MaxSlots = null,
    string? Description = null) : ICommand;

internal sealed class UpdateCarrierTypeCommandHandler : ICommandHandler<UpdateCarrierTypeCommand>
{
    private readonly ICarrierTypeRepository _carrierTypes;

    public UpdateCarrierTypeCommandHandler(ICarrierTypeRepository carrierTypes)
        => _carrierTypes = carrierTypes;

    public async Task<Result> Handle(UpdateCarrierTypeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return Result.Failure("Code is required.");

        var carrierType = await _carrierTypes.GetByCodeAsync(request.Code, cancellationToken);
        if (carrierType is null)
            return Result.Failure($"CarrierType '{request.Code.Trim().ToUpperInvariant()}' not found.");

        // Validation lives in the aggregate and arrives as ArgumentException;
        // caught here rather than left to bubble so an empty display name is a
        // 400 naming the field instead of a scrubbed 500. A FluentValidation
        // validator would not run at all — the Fleet assembly is not registered
        // with AddValidatorsFromAssembly, so it would fail open, silently.
        try
        {
            carrierType.Update(
                request.DisplayName, request.AmrCapability,
                request.MaxWeightKg, request.MaxSlots, request.Description);
        }
        catch (ArgumentException ex) { return Result.Failure(ex.Message); }

        await _carrierTypes.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
