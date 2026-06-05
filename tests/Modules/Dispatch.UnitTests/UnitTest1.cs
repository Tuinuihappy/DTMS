using AMR.DeliveryPlanning.Dispatch.Application.Commands.CancelTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.PauseTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Commands.ResumeTrip;
using AMR.DeliveryPlanning.Dispatch.Application.Services;
using AMR.DeliveryPlanning.Dispatch.Domain.Entities;
using AMR.DeliveryPlanning.Dispatch.Domain.Enums;
using AMR.DeliveryPlanning.Dispatch.Domain.Events;
using AMR.DeliveryPlanning.Dispatch.Domain.Repositories;
using AMR.DeliveryPlanning.SharedKernel.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dispatch.UnitTests;

public class TripTests
{
    private static Trip NewEnvelopeTrip(string upperKey = "abc123-G1") =>
        Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, "ORD-1");

    [Fact]
    public void CreateForEnvelope_ProducesCreatedTripWithUpperKey()
    {
        var trip = NewEnvelopeTrip();

        trip.Status.Should().Be(TripStatus.Created);
        trip.UpperKey.Should().Be("abc123-G1");
        trip.VendorOrderKey.Should().Be("ORD-1");
        trip.JobId.Should().Be(Guid.Empty);
        trip.VehicleId.Should().BeNull();
        trip.Events.Should().ContainSingle(e => e.EventType == "EnvelopeDispatched");
    }

    [Fact]
    public void CreateForEnvelope_AllowsEmptyVendorOrderKey()
    {
        // RIOT3 occasionally returns 200 OK with no orderKey. Correlation
        // still works via UpperKey alone.
        var trip = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", null);

        trip.VendorOrderKey.Should().BeNull();
        trip.UpperKey.Should().Be("abc-G1");
    }

    [Fact]
    public void CreateForEnvelope_RejectsEmptyUpperKey()
    {
        var act = () => Trip.CreateForEnvelope(Guid.NewGuid(), "", "ORD");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MarkVendorStarted_FromCreated_TransitionsToInProgressAndBindsVehicle()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();

        trip.MarkVendorStarted(vehicle);

        trip.Status.Should().Be(TripStatus.InProgress);
        trip.VehicleId.Should().Be(vehicle);
        trip.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkVendorStarted_DuplicateWebhook_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted(Guid.NewGuid());
        var startedAt = trip.StartedAt;

        var act = () => trip.MarkVendorStarted(Guid.NewGuid());

        act.Should().NotThrow();
        trip.StartedAt.Should().Be(startedAt);
    }

    [Fact]
    public void MarkVendorCompleted_FromCreated_CompletesAndFiresEventWithUpperKey()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorCompleted();

        trip.Status.Should().Be(TripStatus.Completed);
        trip.CompletedAt.Should().NotBeNull();
        var evt = trip.DomainEvents.OfType<TripCompletedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.DeliveryOrderId.Should().Be(trip.DeliveryOrderId);
    }

    [Fact]
    public void MarkVendorCompleted_AlreadyCompleted_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();
        var eventCount = trip.DomainEvents.OfType<TripCompletedDomainEvent>().Count();

        var act = () => trip.MarkVendorCompleted();

        act.Should().NotThrow();
        trip.DomainEvents.OfType<TripCompletedDomainEvent>().Count().Should().Be(eventCount);
    }

    [Fact]
    public void MarkVendorCompleted_FromCancelled_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.Cancel("ops");

        var act = () => trip.MarkVendorCompleted();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkVendorFailed_FromCreated_FailsAndFiresEvent()
    {
        var trip = NewEnvelopeTrip("ord-G1");

        trip.MarkVendorFailed("path blocked");

        trip.Status.Should().Be(TripStatus.Failed);
        trip.FailureReason.Should().Be("path blocked");
        var evt = trip.DomainEvents.OfType<TripFailedDomainEvent>().Single();
        evt.VendorUpperKey.Should().Be("ord-G1");
        evt.Reason.Should().Be("path blocked");
    }

    [Fact]
    public void MarkVendorFailed_AlreadyFailed_IsIdempotent()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorFailed("first");
        var act = () => trip.MarkVendorFailed("second");
        act.Should().NotThrow();
        trip.FailureReason.Should().Be("first");
    }

    [Fact]
    public void MarkVendorFailed_FromCompleted_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorCompleted();

        var act = () => trip.MarkVendorFailed("late");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetAssignedVehicle_FirstAssignment_Binds()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();

        trip.SetAssignedVehicle(vehicle);

        trip.VehicleId.Should().Be(vehicle);
    }

    [Fact]
    public void SetAssignedVehicle_SameVehicle_IsNoOp()
    {
        var trip = NewEnvelopeTrip();
        var vehicle = Guid.NewGuid();
        trip.SetAssignedVehicle(vehicle);

        var act = () => trip.SetAssignedVehicle(vehicle);

        act.Should().NotThrow();
    }

    [Fact]
    public void SetAssignedVehicle_DifferentVehicle_Throws()
    {
        var trip = NewEnvelopeTrip();
        trip.SetAssignedVehicle(Guid.NewGuid());

        var act = () => trip.SetAssignedVehicle(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PauseAndResume_RoundTrip()
    {
        var trip = NewEnvelopeTrip();
        trip.MarkVendorStarted();

        trip.Pause();
        trip.Status.Should().Be(TripStatus.Paused);

        trip.Resume();
        trip.Status.Should().Be(TripStatus.InProgress);
    }

    [Fact]
    public void Cancel_FromCreated_Cancels()
    {
        var trip = NewEnvelopeTrip();

        trip.Cancel("operator");

        trip.Status.Should().Be(TripStatus.Cancelled);
    }
}

// ── Handler tests: vendor-first persistence semantics ───────────────────
//
// Cancel/Pause/Resume handlers call the vendor BEFORE persisting the local
// state transition. If the vendor rejects, UpdateAsync must NOT be called
// (the in-memory mutation is discarded by scope disposal). If the domain
// rejects the transition, the vendor must NOT be called.

public class CancelTripCommandHandlerTests
{
    [Fact]
    public async Task VendorSuccess_PersistsCancelledState()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        vendor.CancelCalls.Should().ContainSingle().Which.Should().Be(trip.UpperKey);
    }

    [Fact]
    public async Task VendorFailure_DoesNotPersist()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextResult = Result.Failure("network blip") };

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "ops"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("network blip");
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task DomainReject_DoesNotCallVendor()
    {
        var trip = NewTripInProgress();
        trip.Cancel("first"); // already cancelled
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new CancelTripCommandHandler(repo, vendor, NullLogger<CancelTripCommandHandler>.Instance);
        var result = await handler.Handle(new CancelTripCommand(trip.Id, "second"), default);

        result.IsFailure.Should().BeTrue();
        vendor.CancelCalls.Should().BeEmpty();
        repo.UpdateCalls.Should().Be(0);
    }

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}

public class PauseAndResumeTripHandlerTests
{
    [Fact]
    public async Task Pause_VendorSuccess_PersistsPausedState()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        vendor.PauseCalls.Should().ContainSingle().Which.Should().Be(trip.UpperKey);
    }

    [Fact]
    public async Task Pause_VendorFailure_DoesNotPersist()
    {
        var trip = NewTripInProgress();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps { NextResult = Result.Failure("vendor 500") };

        var handler = new PauseTripCommandHandler(repo, vendor, NullLogger<PauseTripCommandHandler>.Instance);
        var result = await handler.Handle(new PauseTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        repo.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_FromPaused_VendorSuccess_Resumes()
    {
        var trip = NewTripInProgress();
        trip.Pause();
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsSuccess.Should().BeTrue();
        repo.UpdateCalls.Should().Be(1);
        vendor.ResumeCalls.Should().ContainSingle().Which.Should().Be(trip.UpperKey);
    }

    [Fact]
    public async Task Resume_DomainReject_DoesNotCallVendor()
    {
        var trip = NewTripInProgress(); // InProgress, not Paused
        var repo = new FakeTripRepository(trip);
        var vendor = new StubVendorOps();

        var handler = new ResumeTripCommandHandler(repo, vendor, NullLogger<ResumeTripCommandHandler>.Instance);
        var result = await handler.Handle(new ResumeTripCommand(trip.Id), default);

        result.IsFailure.Should().BeTrue();
        vendor.ResumeCalls.Should().BeEmpty();
    }

    private static Trip NewTripInProgress()
    {
        var t = Trip.CreateForEnvelope(Guid.NewGuid(), "abc-G1", "ORD-1");
        t.MarkVendorStarted(Guid.NewGuid());
        return t;
    }
}

// ── Test doubles ────────────────────────────────────────────────────────

internal sealed class FakeTripRepository : ITripRepository
{
    private readonly Trip _trip;
    public int UpdateCalls { get; private set; }

    public FakeTripRepository(Trip trip) { _trip = trip; }

    public Task<Trip?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<Trip?>(id == _trip.Id ? _trip : null);

    public Task<Trip?> GetByUpperKeyAsync(string upperKey, CancellationToken ct = default)
        => Task.FromResult<Trip?>(upperKey == _trip.UpperKey ? _trip : null);

    public Task<List<Trip>> GetActiveTripsByVehicleAsync(Guid vehicleId, CancellationToken ct = default)
        => Task.FromResult(new List<Trip>());

    public Task<List<Trip>> GetInFlightEnvelopeTripsAsync(DateTime staleCutoffUtc, CancellationToken ct = default)
        => Task.FromResult(new List<Trip>());

    public Task AddAsync(Trip trip, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(Trip trip, CancellationToken ct = default)
    {
        UpdateCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class StubVendorOps : IVendorEnvelopeOperationService
{
    public Result NextResult { get; set; } = Result.Success();
    public List<string> CancelCalls { get; } = new();
    public List<string> PauseCalls { get; } = new();
    public List<string> ResumeCalls { get; } = new();

    public Task<Result> CancelAsync(string upperKey, CancellationToken ct = default)
    {
        CancelCalls.Add(upperKey);
        return Task.FromResult(NextResult);
    }

    public Task<Result> PauseAsync(string upperKey, CancellationToken ct = default)
    {
        PauseCalls.Add(upperKey);
        return Task.FromResult(NextResult);
    }

    public Task<Result> ResumeAsync(string upperKey, CancellationToken ct = default)
    {
        ResumeCalls.Add(upperKey);
        return Task.FromResult(NextResult);
    }
}
