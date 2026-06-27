// =============================================================================
// COMMAND + HANDLER TEMPLATE
// =============================================================================
//
// DTMS uses a custom messaging abstraction (NOT MediatR directly) — see
// SharedKernel.Messaging: ICommand, ICommandHandler<T>, Result, Result<T>.
//
// Folder layout convention:
//   src/Modules/{Module}/.../Application/Commands/{CommandName}/
//   ├── {CommandName}Command.cs         ← record with input params
//   └── {CommandName}CommandHandler.cs  ← implements ICommandHandler<T>
//
// Reference examples:
//   src/Modules/Dispatch/.../Commands/PauseTrip/        (state transition + vendor call + idempotent reconcile)
//   src/Modules/Dispatch/.../Commands/CreateEnvelopeTrip/ (creation + outbox)
//   src/Modules/Dispatch/.../Commands/CapturePoD/         (write with attachments)
//
// Return type convention:
//   - Result (void-equivalent): for state transitions
//   - Result<T>: when caller needs returned value (e.g. new aggregate Id)
//   - NEVER throw for expected failure paths — return Result.Failure(...) instead
//
// DELETE THIS COMMENT BLOCK before committing the actual command + handler.
// =============================================================================

// =============================================================================
// FILE 1: {CommandName}Command.cs
// =============================================================================

using DTMS.SharedKernel.Messaging;

namespace DTMS.{Module}.Application.Commands.{CommandName};

/// <summary>
/// {1-line description of intent}
/// </summary>
public record {CommandName}Command(
    Guid {RequiredId},
    string {RequiredField},
    string? OptionalField = null
) : ICommand;
// Or for queries that return a value:
// ) : ICommand<{ReturnType}>;


// =============================================================================
// FILE 2: {CommandName}CommandHandler.cs
// =============================================================================

using DTMS.{Module}.Application.Services;
using DTMS.{Module}.Domain.Entities;
using DTMS.{Module}.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;

namespace DTMS.{Module}.Application.Commands.{CommandName};

/// <summary>
/// {2-4 sentence description of what this handler does, the business intent,
///  any non-obvious sequencing (e.g. "vendor side is source of truth so we
///  call them first and reconcile if their response disagrees").}
/// </summary>
public class {CommandName}CommandHandler : ICommandHandler<{CommandName}Command>
{
    private readonly I{Entity}Repository _repository;
    private readonly IVendorEnvelopeOperationService _vendorOps;      // remove if not needed
    private readonly ILogger<{CommandName}CommandHandler> _logger;

    public {CommandName}CommandHandler(
        I{Entity}Repository repository,
        IVendorEnvelopeOperationService vendorOps,
        ILogger<{CommandName}CommandHandler> logger)
    {
        _repository = repository;
        _vendorOps = vendorOps;
        _logger = logger;
    }

    public async Task<Result> Handle({CommandName}Command request, CancellationToken cancellationToken)
    {
        // ─── 1. Load aggregate ────────────────────────────────────────────
        var entity = await _repository.GetByIdAsync(request.{RequiredId}, cancellationToken);
        if (entity == null)
            return Result.Failure($"{Entity} {request.{RequiredId}} not found.");

        // ─── 2. Pre-conditions ────────────────────────────────────────────
        // Validate caller's intent against current aggregate state.
        // Throw-as-Result so caller sees clear error message.

        if (!entity.CanBe{Action}())
            return Result.Failure($"{Entity} in status {entity.Status} cannot {action}.");

        // ─── 3. Apply state change (in-memory) ────────────────────────────
        // Use try/catch only for domain exceptions, not C# control flow.
        try
        {
            entity.{StateTransitionMethod}(/* params */);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }

        // ─── 4. Side effects (vendor call / outbox / etc.) ────────────────
        // If vendor is source of truth, call vendor BEFORE persisting our
        // state change so we can reconcile their response.

        var vendorResult = await _vendorOps.{Action}Async(entity.VendorKey, cancellationToken);
        if (vendorResult.IsFailure)
        {
            _logger.LogWarning(
                "Vendor {Action} rejected for {Entity} {Id}: {Error}",
                "{Action}", "{Entity}", entity.Id, vendorResult.Error);
            return Result.Failure($"Vendor {action} failed: {vendorResult.Error}");
        }

        // ─── 5. Reconcile vendor drift if needed ──────────────────────────
        // Example: vendor has no record of what we sent → we are out of
        // sync with reality. Mark the aggregate Failed and tell operator
        // what to do next.

        if (vendorResult.Value == VendorOperationOutcome.NoVendorRecord)
        {
            const string reason = "Vendor has no record of this — auto-reconciled.";
            try
            {
                entity.MarkFailed(reason);
                await _repository.UpdateAsync(entity, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Auto-reconcile failed for {Entity} {Id}: {Error}",
                    "{Entity}", entity.Id, ex.Message);
            }
            return Result.Failure("Vendor has no record. Marked Failed; consider /retry.");
        }

        // ─── 6. Persist + log success ─────────────────────────────────────
        await _repository.UpdateAsync(entity, cancellationToken);
        _logger.LogInformation("{Entity} {Id} {action} completed", "{Entity}", entity.Id);

        return Result.Success();
    }
}


// =============================================================================
// FILE 3 (optional): {CommandName}CommandValidator.cs — if using FluentValidation
// =============================================================================

// using FluentValidation;
//
// namespace DTMS.{Module}.Application.Commands.{CommandName};
//
// public class {CommandName}CommandValidator : AbstractValidator<{CommandName}Command>
// {
//     public {CommandName}CommandValidator()
//     {
//         RuleFor(c => c.{RequiredId}).NotEmpty();
//         RuleFor(c => c.{RequiredField}).NotEmpty().MaximumLength(200);
//     }
// }
