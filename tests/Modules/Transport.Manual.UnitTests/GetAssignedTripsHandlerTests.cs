using DTMS.Dispatch.Domain.Enums;
using DTMS.Dispatch.Domain.Repositories;
using DTMS.Transport.Manual.Application.Queries.GetAssignedTrips;
using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Repositories;
using FluentAssertions;
using NSubstitute;

namespace Transport.Manual.UnitTests;

// M48 — after claiming, My trips showed only an 8-char trip id. The list
// now carries what the pool card showed (order ref, pickup → drop, items).
public class GetAssignedTripsHandlerTests
{
    private static ManualTripExtension Assigned(Guid operatorId) =>
        ManualTripExtension.AssignToOperator(
            tripId: Guid.NewGuid(),
            operatorId: operatorId,
            pickupDeadline: DateTime.UtcNow.AddHours(1),
            dropDeadline: DateTime.UtcNow.AddHours(3));

    [Fact]
    public async Task CarriesThePoolCardFields_AndNullsForAMissingTrip()
    {
        var operatorId = Guid.NewGuid();
        var known = Assigned(operatorId);
        var missing = Assigned(operatorId);
        var extensions = Substitute.For<IManualTripExtensionRepository>();
        extensions.GetByOperatorIdAsync(operatorId, Arg.Any<CancellationToken>())
            .Returns(new List<ManualTripExtension> { known, missing });
        var trips = Substitute.For<ITripRepository>();
        trips.GetCardSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TripCardSummary>
            {
                [known.TripId] = new(TripStatus.InProgress, "OD-161026-0306-BUSRUN", "SHELF1", "AMF_01", 8, 0.8),
            });

        var result = await new GetAssignedTripsQueryHandler(extensions, trips)
            .Handle(new GetAssignedTripsQuery(operatorId), CancellationToken.None);

        var dtos = result.Value!.ToDictionary(d => d.TripId);
        dtos[known.TripId].Should().BeEquivalentTo(new
        {
            TripStatus = "InProgress",
            OrderRef = "OD-161026-0306-BUSRUN",
            PickupCode = "SHELF1",
            DropCode = "AMF_01",
            ItemCount = 8,
            TotalWeightKg = 0.8,
        });
        dtos[missing.TripId].Should().BeEquivalentTo(new
        {
            TripStatus = (string?)null,
            OrderRef = (string?)null,
            PickupCode = (string?)null,
            ItemCount = 0,
        });
    }
}
