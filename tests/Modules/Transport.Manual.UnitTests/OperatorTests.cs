using DTMS.Transport.Manual.Domain.Entities;
using DTMS.Transport.Manual.Domain.Enums;
using DTMS.Transport.Manual.Domain.Events;
using FluentAssertions;

namespace Transport.Manual.UnitTests;

public class OperatorTests
{
    private static Operator CreateActive() => Operator.CreateFromJwtClaims(
        employeeCode: "EMP-12345",
        displayName: "Somchai Test",
        role: OperatorRole.Operator,
        primaryWarehouseId: Guid.NewGuid());

    [Fact]
    public void CreateFromJwtClaims_NewOperator_RaisesRegisteredEvent()
    {
        var op = CreateActive();

        op.Id.Should().NotBe(Guid.Empty);
        op.EmployeeCode.Should().Be("EMP-12345");
        op.Status.Should().Be(OperatorStatus.Active);
        op.DomainEvents.Should().ContainSingle(e => e is OperatorRegisteredDomainEvent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateFromJwtClaims_EmptyEmployeeCode_Throws(string? empty)
    {
        var act = () => Operator.CreateFromJwtClaims(empty!, "name", OperatorRole.Operator);
        act.Should().Throw<ArgumentException>().WithParameterName("employeeCode");
    }

    [Fact]
    public void SyncFromJwtClaims_UpdatesDisplayNameAndRole()
    {
        var op = CreateActive();
        var initialSync = op.LastSyncedAt;
        Thread.Sleep(2);     // ensure timestamp advances

        op.SyncFromJwtClaims("Somchai Promoted", OperatorRole.Supervisor);

        op.DisplayName.Should().Be("Somchai Promoted");
        op.Role.Should().Be(OperatorRole.Supervisor);
        op.LastSyncedAt.Should().BeAfter(initialSync);
    }

    [Fact]
    public void AssignToTrip_FirstAssign_SetsCurrentTripAndRaisesEvent()
    {
        var op = CreateActive();
        op.ClearDomainEvents();
        var tripId = Guid.NewGuid();

        op.AssignToTrip(tripId);

        op.CurrentTripId.Should().Be(tripId);
        op.DomainEvents.Should().ContainSingle(e => e is OperatorAssignedToTripDomainEvent);
    }

    [Fact]
    public void AssignToTrip_SameTripTwice_IsIdempotent()
    {
        var op = CreateActive();
        var tripId = Guid.NewGuid();
        op.AssignToTrip(tripId);
        op.ClearDomainEvents();

        op.AssignToTrip(tripId);

        op.CurrentTripId.Should().Be(tripId);
        op.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AssignToTrip_DifferentTripWhileBusy_Throws()
    {
        var op = CreateActive();
        op.AssignToTrip(Guid.NewGuid());

        var act = () => op.AssignToTrip(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already assigned*");
    }

    [Fact]
    public void AssignToTrip_WhenOnLeave_Throws()
    {
        var op = CreateActive();
        op.GoOnLeave("vacation");

        var act = () => op.AssignToTrip(Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not active*");
    }

    [Fact]
    public void ClearTripAssignment_AfterAssign_ClearsAndRaisesEvent()
    {
        var op = CreateActive();
        op.AssignToTrip(Guid.NewGuid());
        op.ClearDomainEvents();

        op.ClearTripAssignment();

        op.CurrentTripId.Should().BeNull();
        op.DomainEvents.Should().ContainSingle(e => e is OperatorReleasedFromTripDomainEvent);
    }

    [Fact]
    public void ClearTripAssignment_WhenIdle_IsNoOp()
    {
        var op = CreateActive();
        op.ClearDomainEvents();

        op.ClearTripAssignment();

        op.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void GoOnLeave_WithActiveTrip_Throws()
    {
        var op = CreateActive();
        op.AssignToTrip(Guid.NewGuid());

        var act = () => op.GoOnLeave("vacation");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*active trip*");
    }

    [Fact]
    public void Deactivate_ClearedTripFirst_Succeeds()
    {
        var op = CreateActive();
        op.AssignToTrip(Guid.NewGuid());
        op.ClearTripAssignment();
        op.ClearDomainEvents();

        op.Deactivate("resigned");

        op.Status.Should().Be(OperatorStatus.Deactivated);
        op.DomainEvents.Should().ContainSingle(e => e is OperatorDeactivatedDomainEvent);
    }

    [Fact]
    public void ReturnFromLeave_AfterDeactivate_Throws()
    {
        var op = CreateActive();
        op.Deactivate("resigned");

        var act = () => op.ReturnFromLeave();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*deactivated*");
    }

    [Fact]
    public void AddCertification_DuplicateActive_IsIdempotent()
    {
        var op = CreateActive();

        op.AddCertification(CertificationType.Hazmat, expiresAt: DateTime.UtcNow.AddYears(1));
        op.AddCertification(CertificationType.Hazmat, expiresAt: DateTime.UtcNow.AddYears(2));

        op.Certifications.Should().ContainSingle(c => c.Type == CertificationType.Hazmat);
    }

    [Fact]
    public void RevokeCertification_FlipsIsActive()
    {
        var op = CreateActive();
        op.AddCertification(CertificationType.Forklift, expiresAt: null);

        op.RevokeCertification(CertificationType.Forklift, "training expired");

        op.Certifications.Single().IsActive.Should().BeFalse();
        op.Certifications.Single().RevokedReason.Should().Be("training expired");
    }

    [Fact]
    public void RegisterPushSubscription_NewEndpoint_AddsRow()
    {
        var op = CreateActive();

        op.RegisterPushSubscription(
            platform: PushPlatform.WebPush,
            endpoint: "https://fcm.googleapis.com/wp/abc",
            publicKey: "pubkey-base64",
            authSecret: "auth-base64",
            deviceLabel: "Chrome on Pixel");

        op.PushSubscriptions.Should().ContainSingle()
          .Which.Endpoint.Should().Be("https://fcm.googleapis.com/wp/abc");
    }

    [Fact]
    public void RegisterPushSubscription_SameEndpoint_UpdatesInPlace()
    {
        var op = CreateActive();
        const string endpoint = "https://fcm.googleapis.com/wp/abc";
        op.RegisterPushSubscription(PushPlatform.WebPush, endpoint, "key1", "secret1", "Chrome");

        op.RegisterPushSubscription(PushPlatform.WebPush, endpoint, "key2", "secret2", "Chrome v2");

        op.PushSubscriptions.Should().ContainSingle();
        op.PushSubscriptions.Single().PublicKey.Should().Be("key2");
        op.PushSubscriptions.Single().DeviceLabel.Should().Be("Chrome v2");
    }

    [Fact]
    public void RegisterPushSubscription_WebPushMissingKeys_Throws()
    {
        var op = CreateActive();

        var act = () => op.RegisterPushSubscription(
            PushPlatform.WebPush,
            endpoint: "https://example",
            publicKey: null,
            authSecret: null,
            deviceLabel: null);

        act.Should().Throw<ArgumentException>();
    }
}
