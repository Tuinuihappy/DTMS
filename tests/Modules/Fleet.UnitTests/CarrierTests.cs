using DTMS.Fleet.Application.Queries.GetCarriers;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Events;
using FluentAssertions;

namespace Fleet.UnitTests;

/// <summary>
/// Guards the rules that make the carrier registry trustworthy: codes are
/// canonical and URL-safe, status transitions are one-way where they need to be,
/// and deletion can never destroy history (ADR-019).
/// </summary>
public class CarrierTests
{
    private static Carrier NewCarrier(string code = "cart-0001")
        => new(code, Guid.NewGuid(), barcode: null, displayName: null,
               currentLocationCode: null, commissionedAt: null, createdBy: "tester");

    // ── code normalization + charset ────────────────────────────────────────

    [Fact]
    public void Register_NormalizesCodeToUpperInvariant()
    {
        NewCarrier("cart-0001").CarrierCode.Should().Be("CART-0001");
    }

    [Theory]
    [InlineData("RACK 1")]      // space breaks the URL path segment
    [InlineData("RACK/1")]      // slash invents an extra segment
    [InlineData("RACK#1")]
    [InlineData("RACK%1")]
    [InlineData("RACK+1")]
    [InlineData("-LEADING")]    // must start alphanumeric
    [InlineData("")]
    [InlineData("   ")]
    public void Register_RejectsCodesThatWouldBreakARoute(string code)
    {
        var act = () => NewCarrier(code);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("CART-0001")]
    [InlineData("CART_0001")]
    [InlineData("CART.0001")]
    [InlineData("C1")]
    public void Register_AcceptsUrlSafeCodes(string code)
    {
        NewCarrier(code).CarrierCode.Should().Be(code);
    }

    [Fact]
    public void Register_StartsAvailableAndStampsCreationAudit()
    {
        var carrier = NewCarrier();

        carrier.Status.Should().Be(CarrierStatus.Available);
        carrier.CreatedBy.Should().Be("tester");
        carrier.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        carrier.RetiredAt.Should().BeNull();
    }

    [Fact]
    public void Register_WithLocation_StampsLastSeenSoTheLocationIsDatable()
    {
        var carrier = new Carrier("CART-1", Guid.NewGuid(), null, null,
            currentLocationCode: "DOCK-A", commissionedAt: null, createdBy: "tester");

        carrier.CurrentLocationCode.Should().Be("DOCK-A");
        carrier.LastSeenAt.Should().NotBeNull();
    }

    // ── maintenance ─────────────────────────────────────────────────────────

    [Fact]
    public void EnterMaintenance_SetsReasonAndRaisesTransition()
    {
        var carrier = NewCarrier();

        carrier.EnterMaintenance("broken wheel", "tech");

        carrier.Status.Should().Be(CarrierStatus.Maintenance);
        carrier.MaintenanceReason.Should().Be("broken wheel");
        carrier.MaintenanceSince.Should().NotBeNull();

        var evt = carrier.DomainEvents.OfType<CarrierStatusChangedDomainEvent>().Single();
        evt.OldStatus.Should().Be(CarrierStatus.Available);
        evt.NewStatus.Should().Be(CarrierStatus.Maintenance);
        evt.Reason.Should().Be("broken wheel");
    }

    [Fact]
    public void EnterMaintenance_Twice_IsRejected()
    {
        var carrier = NewCarrier();
        carrier.EnterMaintenance("broken wheel", "tech");

        var act = () => carrier.EnterMaintenance("again", "tech");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReturnToService_ClearsMaintenanceState()
    {
        var carrier = NewCarrier();
        carrier.EnterMaintenance("broken wheel", "tech");

        carrier.ReturnToService("tech");

        carrier.Status.Should().Be(CarrierStatus.Available);
        carrier.MaintenanceReason.Should().BeNull();
        carrier.MaintenanceSince.Should().BeNull();
    }

    [Fact]
    public void ReturnToService_FromAvailable_IsRejected()
    {
        var act = () => NewCarrier().ReturnToService("tech");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── retire / un-retire ──────────────────────────────────────────────────

    [Fact]
    public void Retire_KeepsTheRowAndRecordsWhy()
    {
        var carrier = NewCarrier();

        carrier.Retire("bent frame", "admin");

        carrier.Status.Should().Be(CarrierStatus.Retired);
        carrier.RetiredAt.Should().NotBeNull();
        carrier.RetireReason.Should().Be("bent frame");
    }

    [Fact]
    public void Retire_WhileUnderMaintenance_ClearsTheMaintenanceState()
    {
        var carrier = NewCarrier();
        carrier.EnterMaintenance("broken wheel", "tech");

        carrier.Retire("not worth fixing", "admin");

        carrier.Status.Should().Be(CarrierStatus.Retired);
        carrier.MaintenanceReason.Should().BeNull();
        carrier.MaintenanceSince.Should().BeNull();
    }

    [Fact]
    public void Retire_Twice_IsRejected()
    {
        var carrier = NewCarrier();
        carrier.Retire("bent frame", "admin");

        var act = () => carrier.Retire("again", "admin");
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>Retiring the wrong carrier must not create a permanently dead
    /// row: it cannot return to service (that path starts from Maintenance) and
    /// it cannot be deleted (that path requires Available), so un-retire is the
    /// only way out.</summary>
    [Fact]
    public void Unretire_ReturnsToAvailableAndClearsRetirement()
    {
        var carrier = NewCarrier();
        carrier.Retire("mis-click", "admin");

        carrier.Unretire("admin");

        carrier.Status.Should().Be(CarrierStatus.Available);
        carrier.RetiredAt.Should().BeNull();
        carrier.RetireReason.Should().BeNull();
    }

    [Fact]
    public void Unretire_WhenNotRetired_IsRejected()
    {
        var act = () => NewCarrier().Unretire("admin");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── location ────────────────────────────────────────────────────────────

    [Fact]
    public void MoveTo_IsAllowedInEveryStatus_BecauseCartsPhysicallyMoveRegardless()
    {
        var retired = NewCarrier();
        retired.Retire("bent frame", "admin");
        retired.MoveTo("SCRAP-YARD", "admin");
        retired.CurrentLocationCode.Should().Be("SCRAP-YARD");

        var serviced = NewCarrier("CART-2");
        serviced.EnterMaintenance("broken wheel", "tech");
        serviced.MoveTo("WORKSHOP", "tech");
        serviced.CurrentLocationCode.Should().Be("WORKSHOP");
    }

    [Fact]
    public void MoveTo_StampsLastSeenAt()
    {
        var carrier = NewCarrier();
        carrier.LastSeenAt.Should().BeNull();

        carrier.MoveTo("DOCK-B", "admin");

        carrier.LastSeenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── delete guard ────────────────────────────────────────────────────────

    [Fact]
    public void CanDelete_AllowsAFreshMistake()
    {
        NewCarrier().CanDelete(maintenanceLogCount: 0).CanDelete.Should().BeTrue();
    }

    [Fact]
    public void CanDelete_RefusesWhenThereIsMaintenanceHistoryToDestroy()
    {
        var (canDelete, reason) = NewCarrier().CanDelete(maintenanceLogCount: 3);

        canDelete.Should().BeFalse();
        reason.Should().Contain("3").And.Contain("Retire");
    }

    [Theory]
    [InlineData(CarrierStatus.Maintenance)]
    [InlineData(CarrierStatus.Retired)]
    public void CanDelete_RefusesAnythingNotAvailable(CarrierStatus status)
    {
        var carrier = NewCarrier();
        if (status == CarrierStatus.Maintenance) carrier.EnterMaintenance("broken", "tech");
        else carrier.Retire("bent frame", "admin");

        carrier.CanDelete(maintenanceLogCount: 0).CanDelete.Should().BeFalse();
    }

    // ── paging contract ─────────────────────────────────────────────────────

    /// <summary>The positional default is a literal because a record's own const
    /// is out of scope there; this keeps the two from drifting apart.</summary>
    [Fact]
    public void GetCarriersQuery_DefaultPageSizeMatchesTheDeclaredConstant()
    {
        new GetCarriersQuery().PageSize.Should().Be(GetCarriersQuery.DefaultPageSize);
    }

    [Fact]
    public void GetCarriersQuery_DefaultPageSizeIsOneTheUiCanActuallyRequest()
    {
        // components/delivery-orders/pagination.tsx offers 10 | 25 | 50 | 100.
        new[] { 10, 25, 50, 100 }.Should().Contain(GetCarriersQuery.DefaultPageSize);
    }
}
