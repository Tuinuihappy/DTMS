// =============================================================================
// UNIT TEST TEMPLATE (Domain + Handler)
// =============================================================================
//
// Stack (per ADR-004):
//   - xUnit  (test runner)
//   - NSubstitute  (mocking)
//   - FluentAssertions  (assertions)
//
// Folder layout:
//   tests/Modules/{Module}.UnitTests/
//   ├── {ModuleName}.UnitTests.csproj
//   ├── {Aggregate}Tests.cs           ← domain aggregate tests
//   ├── {Handler}Tests.cs             ← command/query handler tests
//   ├── Fakes/                        ← inline stubs (per existing convention)
//   │   ├── Fake{Repository}.cs
//   │   └── Stub{Service}.cs
//   └── Builders/                     ← optional fluent builders for complex aggregates
//       └── {Aggregate}Builder.cs
//
// Reference examples:
//   tests/Modules/Dispatch.UnitTests/UnitTest1.cs  (346 LOC — comprehensive trip + handler tests)
//   tests/Modules/Planning.UnitTests/  (12 files — varied patterns)
//
// Naming convention:
//   Class:  {Subject}Tests
//   Method: {Method}_{Scenario}_{ExpectedBehavior}
//   Examples:
//     - CreateForEnvelope_RejectsEmptyUpperKey
//     - MarkVendorStarted_DuplicateWebhook_IsNoOp
//     - PauseTripHandler_WhenNoVendorRecord_AutoMarksFailed
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

using DTMS.{Module}.Application.Commands.{CommandName};
using DTMS.{Module}.Application.Services;
using DTMS.{Module}.Domain.Entities;
using DTMS.{Module}.Domain.Enums;
using DTMS.{Module}.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace {Module}.UnitTests;

// =============================================================================
// DOMAIN AGGREGATE TESTS
// =============================================================================

public class {Aggregate}Tests
{
    // ─── Construction helpers (keep tests focused) ────────────────────────

    private static {Aggregate} NewValid{Aggregate}(
        string? optionalParam = null) =>
        {Aggregate}.Create(
            id: Guid.NewGuid(),
            requiredField: optionalParam ?? "default-value");

    // ─── Factory / Creation ───────────────────────────────────────────────

    [Fact]
    public void Create_ProducesAggregateInInitialState()
    {
        var aggregate = NewValid{Aggregate}();

        aggregate.Status.Should().Be({Aggregate}Status.{InitialState});
        aggregate.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        aggregate.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_RejectsEmptyId()
    {
        var act = () => {Aggregate}.Create(Guid.Empty, "valid");
        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_RejectsBlankRequiredField(string? value)
    {
        var act = () => {Aggregate}.Create(Guid.NewGuid(), value!);
        act.Should().Throw<ArgumentException>().WithParameterName("requiredField");
    }

    [Fact]
    public void Create_RaisesCreatedDomainEvent()
    {
        var aggregate = NewValid{Aggregate}();

        var evt = aggregate.DomainEvents.OfType<{Aggregate}CreatedDomainEvent>().Single();
        evt.{AggregateId}.Should().Be(aggregate.Id);
    }

    // ─── State transitions ────────────────────────────────────────────────

    [Fact]
    public void {Transition}_FromValid_State_Transitions()
    {
        var aggregate = NewValid{Aggregate}();

        aggregate.{TransitionMethod}();

        aggregate.Status.Should().Be({Aggregate}Status.{NextState});
        aggregate.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void {Transition}_AlreadyIn{TargetState}_IsIdempotent()
    {
        var aggregate = NewValid{Aggregate}();
        aggregate.{TransitionMethod}();
        var initialEventCount = aggregate.DomainEvents.OfType<{Aggregate}{Transition}DomainEvent>().Count();

        var act = () => aggregate.{TransitionMethod}();

        act.Should().NotThrow();
        aggregate.DomainEvents.OfType<{Aggregate}{Transition}DomainEvent>().Count()
            .Should().Be(initialEventCount, "idempotent transition should not raise duplicate event");
    }

    [Fact]
    public void {Transition}_FromTerminalState_Throws()
    {
        var aggregate = NewValid{Aggregate}();
        aggregate.MarkFailed("test");

        var act = () => aggregate.{TransitionMethod}();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*from*Failed*");   // matches "Cannot transition from Failed"
    }

    [Theory]
    [InlineData({Aggregate}Status.{StateA}, "{TransitionMethod}", true)]
    [InlineData({Aggregate}Status.{StateB}, "{TransitionMethod}", false)]
    [InlineData({Aggregate}Status.Completed, "{TransitionMethod}", false)]
    public void StateMachine_AllowedTransitions(
        {Aggregate}Status fromStatus,
        string action,
        bool shouldSucceed)
    {
        // Build aggregate in `fromStatus` (use a state-builder helper if needed)
        var aggregate = new {Aggregate}Builder().InStatus(fromStatus).Build();

        var act = () => InvokeAction(aggregate, action);

        if (shouldSucceed)
            act.Should().NotThrow();
        else
            act.Should().Throw<InvalidOperationException>();
    }

    private static void InvokeAction({Aggregate} aggregate, string action) => action switch
    {
        "{TransitionMethod}" => aggregate.{TransitionMethod}(),
        _ => throw new ArgumentException($"Unknown action: {action}")
    };
}


// =============================================================================
// COMMAND HANDLER TESTS
// =============================================================================

public class {CommandName}CommandHandlerTests
{
    private readonly I{Entity}Repository _repository;
    private readonly IVendorEnvelopeOperationService _vendorOps;
    private readonly {CommandName}CommandHandler _handler;

    public {CommandName}CommandHandlerTests()
    {
        _repository = Substitute.For<I{Entity}Repository>();
        _vendorOps = Substitute.For<IVendorEnvelopeOperationService>();
        _handler = new {CommandName}CommandHandler(
            _repository,
            _vendorOps,
            NullLogger<{CommandName}CommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenEntityNotFound_ReturnsFailure()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(({Entity}?)null);

        var result = await _handler.Handle(
            new {CommandName}Command(Guid.NewGuid(), "field"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_HappyPath_PersistsAndReturnsSuccess()
    {
        var entity = NewEntityInValidState();
        _repository.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        _vendorOps.{Action}Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<VendorOperationOutcome>.Success(VendorOperationOutcome.Accepted));

        var result = await _handler.Handle(
            new {CommandName}Command(entity.Id, "field"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repository.Received(1).UpdateAsync(entity, Arg.Any<CancellationToken>());
        entity.Status.Should().Be({Entity}Status.{ExpectedState});
    }

    [Fact]
    public async Task Handle_WhenVendorRejects_ReturnsFailure_DoesNotPersist()
    {
        var entity = NewEntityInValidState();
        _repository.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        _vendorOps.{Action}Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<VendorOperationOutcome>.Failure("vendor down"));

        var result = await _handler.Handle(
            new {CommandName}Command(entity.Id, "field"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<{Entity}>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenVendorReportsNoRecord_AutoReconcilesToFailed()
    {
        var entity = NewEntityInValidState();
        _repository.GetByIdAsync(entity.Id, Arg.Any<CancellationToken>()).Returns(entity);
        _vendorOps.{Action}Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<VendorOperationOutcome>.Success(VendorOperationOutcome.NoVendorRecord));

        var result = await _handler.Handle(
            new {CommandName}Command(entity.Id, "field"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        entity.Status.Should().Be({Entity}Status.Failed);
        await _repository.Received(1).UpdateAsync(entity, Arg.Any<CancellationToken>());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static {Entity} NewEntityInValidState()
    {
        var entity = {Entity}.Create(Guid.NewGuid(), "test");
        // Drive to the state this handler expects as input
        entity.{TransitionToValidState}();
        return entity;
    }
}


// =============================================================================
// OPTIONAL: Builder pattern for complex aggregates (in tests/Builders/)
// =============================================================================

public sealed class {Aggregate}Builder
{
    private Guid _id = Guid.NewGuid();
    private string _name = "default";
    private {Aggregate}Status _status = {Aggregate}Status.{InitialState};

    public {Aggregate}Builder WithId(Guid id) { _id = id; return this; }
    public {Aggregate}Builder Named(string name) { _name = name; return this; }
    public {Aggregate}Builder InStatus({Aggregate}Status status) { _status = status; return this; }

    public {Aggregate} Build()
    {
        var aggregate = {Aggregate}.Create(_id, _name);
        // Drive to target status if not InitialState
        DriveToStatus(aggregate, _status);
        return aggregate;
    }

    private static void DriveToStatus({Aggregate} a, {Aggregate}Status target)
    {
        // Apply transitions to reach `target`
        if (target == {Aggregate}Status.InProgress) a.MarkStarted();
        else if (target == {Aggregate}Status.Completed) { a.MarkStarted(); a.MarkCompleted(); }
        else if (target == {Aggregate}Status.Failed) a.MarkFailed("test");
        // ... etc
    }
}
