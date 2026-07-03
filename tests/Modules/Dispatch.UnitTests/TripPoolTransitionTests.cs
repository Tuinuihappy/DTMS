using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Events;
using DTMS.Dispatch.IntegrationEvents;
using FluentAssertions;

namespace Dispatch.UnitTests;

// WMS PR-4b (PR-F) — Locks down the Trip.MarkDispatched semantics that
// power the Manual/Fleet pool. The transient "Dispatched" status is
// gone (PR-C.1); pool membership is derived from
// (Status = Created ∧ DispatchedAt IS NOT NULL ∧ ClaimedByOperatorId IS NULL).
//
// Contract this locks down:
//   1. Sets DispatchedAt to a UTC timestamp on the first call
//   2. Leaves Status = Created (parity with AMR — the trip is not "InProgress"
//      until an operator claims it)
//   3. Emits exactly one TripDispatchedDomainEvent per trip (idempotent)
//   4. Rejects Manual reset attempts on a trip past Created (e.g. someone
//      calling MarkDispatched on an already-claimed / already-completed trip)
public class TripPoolTransitionTests
{
    [Fact]
    public void MarkDispatched_HappyPath_StampsDispatchedAt_KeepsStatusCreated()
    {
        var trip = FreshManualTrip();

        trip.MarkDispatched();

        trip.Status.Should().Be(TripStatus.Created, "pool trips stay Created until an operator claims — parity with AMR");
        trip.DispatchedAt.Should().NotBeNull("DispatchedAt is the pool signal");
        trip.DispatchedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
        trip.DispatchedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
        trip.ClaimedByOperatorId.Should().BeNull("no operator has claimed yet");
    }

    [Fact]
    public void MarkDispatched_FiresExactlyOneDomainEvent_WithItemsSnapshot()
    {
        var trip = FreshManualTrip();
        var items = new[]
        {
            new TripItemSnapshot(
                ItemPk: Guid.NewGuid(), ItemSeq: 1, LotNo: "L1", ItemStatus: "Pending",
                PickupCode: "WH-A", DropCode: "WH-B", WeightKg: 12.5,
                DeliveryOrderId: trip.DeliveryOrderId,
                OrderRef: "OD-0001", OrderStatus: "Confirmed"),
        };

        trip.MarkDispatched(items);

        var events = trip.DomainEvents.OfType<TripDispatchedDomainEvent>().ToList();
        events.Should().HaveCount(1);
        events[0].TripId.Should().Be(trip.Id);
        events[0].DeliveryOrderId.Should().Be(trip.DeliveryOrderId);
        events[0].Items.Should().BeEquivalentTo(items);
    }

    [Fact]
    public void MarkDispatched_Idempotent_SecondCallIsNoOp()
    {
        var trip = FreshManualTrip();
        trip.MarkDispatched();
        var firstDispatchedAt = trip.DispatchedAt;
        trip.ClearDomainEventsForTest();

        trip.MarkDispatched();   // MassTransit redelivery / retry path

        trip.DispatchedAt.Should().Be(firstDispatchedAt, "DispatchedAt is the idempotency key");
        trip.DomainEvents.OfType<TripDispatchedDomainEvent>().Should().BeEmpty(
            "no second TripDispatchedDomainEvent — the pool would insert two cards");
    }

    [Fact]
    public void MarkDispatched_OnAmrTripAlreadyInProgress_Throws()
    {
        // An AMR trip never passes through the pool: RIOT3 fires
        // TASK_PROCESSING → MarkVendorStarted → Status=InProgress with
        // DispatchedAt still null. If a caller (buggy dispatch retry?)
        // then tries to force-drop it into the pool, we want a loud error
        // rather than a silent no-op — the idempotency check is keyed on
        // DispatchedAt, so it doesn't fire here.
        var amrTrip = Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(),
            upperKey: "UK-AMR-BAD",
            vendorOrderKey: "RIOT-KEY");
        amrTrip.MarkVendorStarted(
            vehicleId: null, vendorVehicleKey: "veh-1", vendorVehicleName: "V1");
        amrTrip.Status.Should().Be(TripStatus.InProgress);
        amrTrip.DispatchedAt.Should().BeNull();

        var reDispatch = () => amrTrip.MarkDispatched();

        reDispatch.Should().Throw<InvalidOperationException>()
            .WithMessage("*MarkDispatched requires Created status*");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Trip FreshManualTrip() =>
        Trip.CreateForEnvelope(
            deliveryOrderId: Guid.NewGuid(),
            upperKey: "UK-" + Guid.NewGuid().ToString("N")[..8],
            vendorOrderKey: null);
}

// Small test-only shim so the idempotency test can inspect the second
// call in isolation. DomainEvents is a read-only collection on the
// aggregate; adding a test-only clear is safer than resorting to
// reflection on every case.
internal static class TripDomainEventsTestExtensions
{
    public static void ClearDomainEventsForTest(this Trip trip)
    {
        var method = typeof(Trip).GetMethod(
            "ClearDomainEvents",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        method?.Invoke(trip, null);
    }
}
