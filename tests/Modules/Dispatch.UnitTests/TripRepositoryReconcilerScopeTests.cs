using DTMS.Dispatch.Domain.Entities;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Dispatch.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dispatch.UnitTests;

// The RIOT3 reconciler's two queries must only return trips that were actually
// sent to RIOT3. Every trip carries an UpperKey (CreateForEnvelope requires
// one), so the old `UpperKey != ""` filter let Manual trips through: on
// 2026-09-24 all 29 trips behind the "AMR trips stuck" P1 were unclosed Manual
// jobs, polled against RIOT3 every minute. VendorRequestSnapshot is set only on
// the AMR dispatch path.
public class TripRepositoryReconcilerScopeTests
{
    private static DispatchDbContext NewDb()
        => new(new DbContextOptionsBuilder<DispatchDbContext>()
            .UseInMemoryDatabase("dispatch-" + Guid.NewGuid()).Options);

    // What DispatchOrderTemplateService creates after RIOT3 accepts the order.
    private static Trip AmrTrip(string upperKey, string? vendorOrderKey = "RIOT3-ABC")
        => Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, vendorOrderKey,
            vendorRequestSnapshot: "{\"upperKey\":\"" + upperKey + "\"}");

    // What ManualDispatchStrategy / SelfManagedDispatchStrategy create.
    private static Trip ManualTrip(string upperKey)
        => Trip.CreateForEnvelope(Guid.NewGuid(), upperKey, vendorOrderKey: null,
            vendorRequestSnapshot: null);

    private static async Task<TripRepository> Seed(DispatchDbContext db, params Trip[] trips)
    {
        db.Trips.AddRange(trips);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new TripRepository(db);
    }

    [Fact]
    public async Task InFlight_ExcludesManualTrips()
    {
        var amr = AmrTrip("amr-G1");
        amr.MarkVendorStarted();
        var manual = ManualTrip("manual-G1");
        manual.MarkVendorStarted();
        await using var db = NewDb();
        var repo = await Seed(db, amr, manual);

        var inFlight = await repo.GetInFlightEnvelopeTripsAsync();

        inFlight.Select(t => t.UpperKey).Should().Equal("amr-G1");
    }

    [Fact]
    public async Task InFlight_KeepsAmrTripWithoutVendorOrderKey()
    {
        // RIOT3 may accept an order without echoing an orderKey, leaving the
        // trip with no AmrExtension. It was still sent to RIOT3, so the
        // reconciler must keep polling it.
        var amr = AmrTrip("amr-G1", vendorOrderKey: null);
        await using var db = NewDb();
        var repo = await Seed(db, amr);

        var inFlight = await repo.GetInFlightEnvelopeTripsAsync();

        inFlight.Should().ContainSingle(t => t.UpperKey == "amr-G1");
    }

    [Fact]
    public async Task TerminalMissingVehicle_ExcludesManualTrips()
    {
        var amr = AmrTrip("amr-G1");
        amr.MarkVendorStarted();
        amr.MarkVendorCompleted();
        var manual = ManualTrip("manual-G1");
        manual.MarkVendorStarted();
        manual.MarkVendorCompleted();
        await using var db = NewDb();
        var repo = await Seed(db, amr, manual);

        var trips = await repo.GetTerminalTripsMissingVehicleAsync(DateTime.UtcNow.AddHours(-1));

        trips.Select(t => t.UpperKey).Should().Equal("amr-G1");
    }
}
