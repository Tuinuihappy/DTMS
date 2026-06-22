// =============================================================================
// DOMAIN AGGREGATE ROOT TEMPLATE
// =============================================================================
//
// Filename: src/Modules/{Module}/AMR.DeliveryPlanning.{Module}.Domain/Entities/{Name}.cs
//
// Use this template when creating a new domain aggregate root.
//
// DDD conventions enforced in DTMS (see existing Trip, DeliveryOrder, Operator):
//   - Inherit from SharedKernel's AggregateRoot<TId>
//   - Private parameterless constructor for EF Core
//   - Static factory method(s) for creation (Create / CreateForXxx)
//   - All properties have PRIVATE setters — mutation only through methods
//   - Public methods enforce invariants and raise domain events
//   - Collections exposed as IReadOnlyCollection, backed by private List
//   - Validation throws ArgumentException (constructor input) or
//     InvalidOperationException (state transition)
//
// Reference examples:
//   src/Modules/Dispatch/.../Domain/Entities/Trip.cs (full DDD aggregate)
//   src/Modules/DeliveryOrder/.../Domain/Entities/DeliveryOrder.cs
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

using AMR.DeliveryPlanning.{Module}.Domain.Enums;
using AMR.DeliveryPlanning.{Module}.Domain.Events;
using AMR.DeliveryPlanning.SharedKernel.Domain;

namespace AMR.DeliveryPlanning.{Module}.Domain.Entities;

/// <summary>
/// {1-2 sentence description of what this aggregate represents and why it exists.
/// Mention any non-obvious invariants up front.}
///
/// Lifecycle: {state machine summary if applicable — Created → InProgress → Completed}
/// </summary>
public class {AggregateName} : AggregateRoot<Guid>
{
    // ─── State ────────────────────────────────────────────────────────────
    // Group related fields together. Comment WHY each field exists if the
    // reason isn't obvious from the name.

    public Guid {ForeignKeyId} { get; private set; }
    public {AggregateName}Status Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Optional fields — explain nullability reason
    public string? OptionalField { get; private set; }   // null until {when set}

    // ─── Collections (private backing) ────────────────────────────────────

    private readonly List<{ChildEntity}> _children = new();
    public IReadOnlyCollection<{ChildEntity}> Children => _children.AsReadOnly();

    // ─── EF Core constructor (DO NOT use elsewhere) ───────────────────────

    private {AggregateName}() { }

    // ─── Factory methods ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a new {AggregateName} in {InitialState} state.
    /// </summary>
    /// <exception cref="ArgumentException">When {required field} is invalid</exception>
    public static {AggregateName} Create(
        Guid {requiredId},
        string {requiredField},
        // ... other required params
        DateTime? createdAt = null)
    {
        if ({requiredId} == Guid.Empty)
            throw new ArgumentException("Required ID cannot be empty", nameof({requiredId}));

        if (string.IsNullOrWhiteSpace({requiredField}))
            throw new ArgumentException("Field cannot be empty", nameof({requiredField}));

        var aggregate = new {AggregateName}
        {
            Id = Guid.NewGuid(),
            {ForeignKeyId} = {requiredId},
            Status = {AggregateName}Status.{InitialState},
            CreatedAt = createdAt ?? DateTime.UtcNow
        };

        // Optionally raise a domain event for creation
        aggregate.AddDomainEvent(new {AggregateName}CreatedDomainEvent(aggregate.Id, /* ... */));

        return aggregate;
    }

    // ─── State transition methods ─────────────────────────────────────────
    // Each method:
    //   1. Validates invariants (throw InvalidOperationException if violated)
    //   2. Mutates state
    //   3. Records timestamp
    //   4. Raises domain event
    //
    // Idempotency: prefer no-op return over exception when caller is
    // potentially a webhook / event handler that may replay. Be explicit
    // in the comment if that's the case.

    /// <summary>
    /// Transitions to {NextState}. Idempotent if already in {NextState}.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If current Status is not one of {valid source states}.
    /// </exception>
    public void {TransitionMethodName}(/* params */)
    {
        // Idempotent guard (if applicable)
        if (Status == {AggregateName}Status.{TargetState})
            return;

        // Invariant check
        if (Status != {AggregateName}Status.{ExpectedSourceState})
            throw new InvalidOperationException(
                $"Cannot {transition} from {Status}. Expected: {{ExpectedSourceState}}.");

        // Mutate
        Status = {AggregateName}Status.{TargetState};
        UpdatedAt = DateTime.UtcNow;

        // Raise event
        AddDomainEvent(new {AggregateName}{Transition}DomainEvent(Id, /* ... */));
    }

    /// <summary>
    /// {Another state transition — e.g. MarkFailed, Cancel}
    /// </summary>
    public void MarkFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason required", nameof(reason));

        // Terminal state guard
        if (Status is {AggregateName}Status.Completed or {AggregateName}Status.Cancelled)
            throw new InvalidOperationException(
                $"Cannot fail {nameof({AggregateName})} from terminal state {Status}");

        // Idempotent — preserve first failure reason
        if (Status == {AggregateName}Status.Failed)
            return;

        Status = {AggregateName}Status.Failed;
        OptionalField = reason;
        UpdatedAt = DateTime.UtcNow;

        AddDomainEvent(new {AggregateName}FailedDomainEvent(Id, reason));
    }

    // ─── Child entity management ──────────────────────────────────────────

    public void Add{ChildEntity}({ChildEntity} child)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));

        // Invariant: e.g. max count, no duplicates, only valid in certain states
        if (_children.Count >= MAX_CHILDREN)
            throw new InvalidOperationException($"Cannot exceed {MAX_CHILDREN} children");

        _children.Add(child);
        UpdatedAt = DateTime.UtcNow;
    }

    // ─── Query helpers (computed, no state mutation) ──────────────────────

    public bool CanBe{Action}() => Status is {AggregateName}Status.{StateA} or {AggregateName}Status.{StateB};

    public bool IsInTerminalState() =>
        Status is {AggregateName}Status.Completed
                or {AggregateName}Status.Failed
                or {AggregateName}Status.Cancelled;

    private const int MAX_CHILDREN = 100;
}
