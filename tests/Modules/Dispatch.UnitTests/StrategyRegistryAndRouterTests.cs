using DTMS.DeliveryOrder.Domain.Enums;
using DTMS.Dispatch.Application.Services;
using DTMS.Dispatch.Domain.Entities;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using NSubstitute;

namespace Dispatch.UnitTests;

// Phase 1 foundation: verify the new IDispatchStrategyRegistry +
// IVendorOperationsRouter behave per ADR-001 (mode-disabled throws
// typed exception; duplicate registrations fail at construction).
//
// These tests use minimal stubs — we're proving the registry/router
// plumbing, not the AMR adapter itself (which has its own coverage).

public class DispatchStrategyRegistryTests
{
    [Fact]
    public void Get_RegisteredMode_ReturnsStrategy()
    {
        var amrStrategy = StubStrategy(TransportMode.Amr);
        var registry = new DispatchStrategyRegistry(new[] { amrStrategy });

        registry.Get(TransportMode.Amr).Should().BeSameAs(amrStrategy);
    }

    [Fact]
    public void Get_UnregisteredMode_ThrowsTransportModeNotEnabled()
    {
        var registry = new DispatchStrategyRegistry(new[] { StubStrategy(TransportMode.Amr) });

        var act = () => registry.Get(TransportMode.Manual);

        act.Should().Throw<TransportModeNotEnabledException>()
            .Which.Mode.Should().Be(TransportMode.Manual);
    }

    [Fact]
    public void IsRegistered_ReturnsTrue_OnlyForRegisteredModes()
    {
        var registry = new DispatchStrategyRegistry(new[] { StubStrategy(TransportMode.Amr) });

        registry.IsRegistered(TransportMode.Amr).Should().BeTrue();
        registry.IsRegistered(TransportMode.Manual).Should().BeFalse();
        registry.IsRegistered(TransportMode.Fleet).Should().BeFalse();
    }

    [Fact]
    public void Constructor_DuplicateMode_ThrowsAtBuildTime()
    {
        // Two strategies both claiming AMR = programming error. We surface
        // it at startup (DI build) instead of silently using the last one.
        var first = StubStrategy(TransportMode.Amr);
        var second = StubStrategy(TransportMode.Amr);

        var act = () => new DispatchStrategyRegistry(new[] { first, second });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple IDispatchStrategy registrations for mode Amr*");
    }

    [Fact]
    public void Constructor_NoStrategies_BuildsEmptyRegistry()
    {
        // Edge case: no modes enabled — registry builds, but Get throws
        // for every mode. Useful for misconfigured staging environments.
        var registry = new DispatchStrategyRegistry(Array.Empty<IDispatchStrategy>());

        registry.IsRegistered(TransportMode.Amr).Should().BeFalse();
        registry.Invoking(r => r.Get(TransportMode.Amr))
            .Should().Throw<TransportModeNotEnabledException>();
    }

    private static IDispatchStrategy StubStrategy(TransportMode mode)
    {
        var stub = Substitute.For<IDispatchStrategy>();
        stub.Mode.Returns(mode);
        return stub;
    }
}

public class VendorOperationsRouterTests
{
    [Fact]
    public void For_AmrMode_ReturnsMarkedAmrAdapter()
    {
        var amrEnvelope = StubEnvelopeAdapter(TransportMode.Amr);
        var router = new VendorOperationsRouter(
            new[] { amrEnvelope },
            Array.Empty<IVendorRobotOperationService>());

        router.For(TransportMode.Amr).Should().BeSameAs(amrEnvelope);
    }

    [Fact]
    public void For_ManualMode_ThrowsWhenNoAdapterRegistered()
    {
        var router = new VendorOperationsRouter(
            new[] { StubEnvelopeAdapter(TransportMode.Amr) },
            Array.Empty<IVendorRobotOperationService>());

        var act = () => router.For(TransportMode.Manual);

        act.Should().Throw<TransportModeNotEnabledException>()
            .Which.Mode.Should().Be(TransportMode.Manual);
    }

    [Fact]
    public void ForRobot_AmrMode_ReturnsMarkedRobotAdapter()
    {
        var amrRobot = StubRobotAdapter(TransportMode.Amr);
        var router = new VendorOperationsRouter(
            Array.Empty<IVendorEnvelopeOperationService>(),
            new[] { amrRobot });

        router.ForRobot(TransportMode.Amr).Should().BeSameAs(amrRobot);
    }

    [Fact]
    public void ForRobot_ManualMode_ReturnsNull_NotThrows()
    {
        // Robot operations are AMR-specific. Manual / Fleet legitimately
        // have no robot to nudge — router returns null instead of
        // throwing so callers can hide UI elements gracefully.
        var router = new VendorOperationsRouter(
            Array.Empty<IVendorEnvelopeOperationService>(),
            new[] { StubRobotAdapter(TransportMode.Amr) });

        router.ForRobot(TransportMode.Manual).Should().BeNull();
    }

    [Fact]
    public void Constructor_AdaptersWithoutMarker_AreSkipped()
    {
        // A bare IVendorEnvelopeOperationService without the marker
        // (e.g. test fakes) should be ignored by the router — not throw
        // and not be reachable via For(). This keeps test setups working
        // even when they register non-marked stubs.
        var marked = StubEnvelopeAdapter(TransportMode.Amr);
        var unmarked = Substitute.For<IVendorEnvelopeOperationService>();   // no marker

        var router = new VendorOperationsRouter(
            new[] { marked, unmarked },
            Array.Empty<IVendorRobotOperationService>());

        router.For(TransportMode.Amr).Should().BeSameAs(marked);
    }

    [Fact]
    public void Constructor_DuplicateMarkedAdapters_ThrowsAtBuildTime()
    {
        var firstAmr = StubEnvelopeAdapter(TransportMode.Amr);
        var secondAmr = StubEnvelopeAdapter(TransportMode.Amr);

        var act = () => new VendorOperationsRouter(
            new[] { firstAmr, secondAmr },
            Array.Empty<IVendorRobotOperationService>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Multiple IVendorEnvelopeOperationService registrations for mode Amr*");
    }

    private static IVendorEnvelopeOperationService StubEnvelopeAdapter(TransportMode mode)
    {
        // Combine envelope service + marker via mock that implements both
        var stub = Substitute.For<IVendorEnvelopeOperationService, IVendorOperationsAdapter>();
        ((IVendorOperationsAdapter)stub).Mode.Returns(mode);
        return stub;
    }

    private static IVendorRobotOperationService StubRobotAdapter(TransportMode mode)
    {
        var stub = Substitute.For<IVendorRobotOperationService, IVendorOperationsAdapter>();
        ((IVendorOperationsAdapter)stub).Mode.Returns(mode);
        return stub;
    }
}
