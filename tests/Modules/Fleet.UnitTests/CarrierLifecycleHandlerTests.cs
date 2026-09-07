using DTMS.Fleet.Application.Commands.DeleteCarrier;
using DTMS.Fleet.Application.Commands.RetireCarrier;
using DTMS.Fleet.Application.Commands.ReturnCarrierToService;
using DTMS.Fleet.Application.Commands.SetCarrierMaintenance;
using DTMS.Fleet.Domain.Entities;
using DTMS.Fleet.Domain.Enums;
using DTMS.Fleet.Domain.Events;
using DTMS.Fleet.Domain.Repositories;
using DTMS.SharedKernel.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Fleet.UnitTests;

/// <summary>
/// Covers what the aggregate's own tests cannot: that the carrier's status and
/// its maintenance log move together, that retiring never strands an open
/// episode, and that deletion consults real history before destroying a row.
/// </summary>
public class CarrierLifecycleHandlerTests
{
    private const string Code = "CART-0001";

    // ── maintenance opens and closes an episode ─────────────────────────────

    [Fact]
    public async Task SetMaintenance_OpensAnEpisodeAndSavesOnce()
    {
        var (carriers, logs, actor) = Mocks();
        var carrier = NewCarrier();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);

        var result = await new SetCarrierMaintenanceCommandHandler(carriers, logs, actor)
            .Handle(new SetCarrierMaintenanceCommand(Code, "broken wheel"), default);

        result.IsSuccess.Should().BeTrue();
        carrier.Status.Should().Be(CarrierStatus.Maintenance);
        await logs.Received(1).AddAsync(
            Arg.Is<CarrierMaintenanceLog>(l => l.Reason == "broken wheel" && l.IsOpen),
            Arg.Any<CancellationToken>());
        // One save for both writes — the repositories share a scoped DbContext,
        // so saving through each would split one state change into two commits.
        await carriers.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetMaintenance_OnAnAlreadyServicedCarrier_FailsWithoutWritingALog()
    {
        var (carriers, logs, actor) = Mocks();
        var carrier = NewCarrier();
        carrier.EnterMaintenance("first", "tech");
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);

        var result = await new SetCarrierMaintenanceCommandHandler(carriers, logs, actor)
            .Handle(new SetCarrierMaintenanceCommand(Code, "second"), default);

        result.IsSuccess.Should().BeFalse();
        await logs.DidNotReceive().AddAsync(Arg.Any<CarrierMaintenanceLog>(), Arg.Any<CancellationToken>());
        await carriers.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReturnToService_ClosesTheOpenEpisodeWithOutcomeAndActor()
    {
        var (carriers, logs, actor) = Mocks();
        var carrier = NewCarrier();
        carrier.EnterMaintenance("broken wheel", "tech");
        var episode = new CarrierMaintenanceLog(carrier.Id, "broken wheel", "tech");

        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.GetOpenForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(episode);

        var result = await Handler(carriers, logs, actor)
            .Handle(new ReturnCarrierToServiceCommand(Code, "wheel replaced"), default);

        result.IsSuccess.Should().BeTrue();
        carrier.Status.Should().Be(CarrierStatus.Available);
        episode.IsOpen.Should().BeFalse();
        episode.Outcome.Should().Be("wheel replaced");
        episode.EndedBy.Should().Be("Ada Ops");
    }

    /// <summary>Legacy rows, or an episode closed out of band. The carrier's real
    /// status is what the floor depends on, so it comes back regardless — the
    /// gap is logged rather than blocking the workshop.</summary>
    [Fact]
    public async Task ReturnToService_WithNoOpenEpisode_StillRestoresTheCarrier()
    {
        var (carriers, logs, actor) = Mocks();
        var carrier = NewCarrier();
        carrier.EnterMaintenance("broken wheel", "tech");

        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.GetOpenForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>())
            .Returns((CarrierMaintenanceLog?)null);

        var result = await Handler(carriers, logs, actor)
            .Handle(new ReturnCarrierToServiceCommand(Code), default);

        result.IsSuccess.Should().BeTrue();
        carrier.Status.Should().Be(CarrierStatus.Available);
    }

    // ── retire must not strand an open episode ──────────────────────────────

    /// <summary>An episode left open would hold the partial unique index
    /// forever, so a carrier that was un-retired could never be serviced again.</summary>
    [Fact]
    public async Task Retire_WhileUnderMaintenance_ClosesTheOpenEpisode()
    {
        var (carriers, logs, actor) = Mocks();
        var carrier = NewCarrier();
        carrier.EnterMaintenance("broken wheel", "tech");
        var episode = new CarrierMaintenanceLog(carrier.Id, "broken wheel", "tech");

        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.GetOpenForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(episode);

        var result = await new RetireCarrierCommandHandler(carriers, logs, actor)
            .Handle(new RetireCarrierCommand(Code, "not worth fixing"), default);

        result.IsSuccess.Should().BeTrue();
        carrier.Status.Should().Be(CarrierStatus.Retired);
        episode.IsOpen.Should().BeFalse();
        episode.Outcome.Should().Contain("Retired");
    }

    [Fact]
    public async Task Retire_WithoutReason_IsRefused()
    {
        var (carriers, logs, actor) = Mocks();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(NewCarrier());

        var result = await new RetireCarrierCommandHandler(carriers, logs, actor)
            .Handle(new RetireCarrierCommand(Code, "  "), default);

        result.IsSuccess.Should().BeFalse();
        await carriers.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── delete consults real history ────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesAFreshMistake()
    {
        var (carriers, logs, _) = Mocks();
        var carrier = NewCarrier();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.CountForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(0);

        var result = await DeleteHandler(carriers, logs)
            .Handle(new DeleteCarrierCommand(Code), default);

        result.Value.Outcome.Should().Be(DeleteCarrierOutcome.Deleted);
        carriers.Received(1).Remove(carrier);
    }

    [Fact]
    public async Task Delete_RefusesWhenHistoryWouldBeDestroyed_AndSaysWhy()
    {
        var (carriers, logs, _) = Mocks();
        var carrier = NewCarrier();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.CountForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(3);

        var result = await DeleteHandler(carriers, logs)
            .Handle(new DeleteCarrierCommand(Code), default);

        result.Value.Outcome.Should().Be(DeleteCarrierOutcome.Blocked);
        result.Value.Reason.Should().Contain("3").And.Contain("Retire");
        carriers.DidNotReceive().Remove(Arg.Any<Carrier>());
        await carriers.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_OfAnUnknownCode_IsNotFoundRatherThanAnError()
    {
        var (carriers, logs, _) = Mocks();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns((Carrier?)null);

        var result = await DeleteHandler(carriers, logs)
            .Handle(new DeleteCarrierCommand(Code), default);

        result.Value.Outcome.Should().Be(DeleteCarrierOutcome.NotFound);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static Carrier NewCarrier()
        => new(Code, Guid.NewGuid(), null, null, null, null, "tester");

    private static ReturnCarrierToServiceCommandHandler Handler(
        ICarrierRepository carriers, ICarrierMaintenanceLogRepository logs, ICurrentActorContext actor)
        => new(carriers, logs, actor,
               NullLogger<ReturnCarrierToServiceCommandHandler>.Instance);

    // ── delete takes the carrier's images with it ───────────────────────────

    [Fact]
    public async Task Delete_TakesTheCarriersImagesWithIt()
    {
        var (carriers, logs, _) = Mocks();
        var carrier = NewCarrier();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.CountForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(0);

        // A photo is not the kind of history CanDelete protects, so it never
        // blocks a delete — which is exactly why a deletable carrier can have
        // some, and why they cannot be left to the database cascade.
        var photo = PhotoOf(carrier.Id);
        var attachments = Substitute.For<IAttachmentRepository>();
        attachments.ListForCarrierForDeleteAsync(carrier.Id, Arg.Any<CancellationToken>())
                   .Returns([photo]);

        var result = await DeleteHandler(carriers, logs, attachments)
            .Handle(new DeleteCarrierCommand(Code), default);

        result.Value.Outcome.Should().Be(DeleteCarrierOutcome.Deleted);

        // Left on the aggregate for the interceptor to turn into an outbox row
        // during SaveChanges. A cascade would have removed the row below EF,
        // firing nothing and stranding the bytes.
        photo.DomainEvents.OfType<AttachmentObjectsOrphanedDomainEvent>()
             .Should().ContainSingle()
             .Which.ObjectKeys.Should().BeEquivalentTo(photo.AllObjectKeys());

        attachments.Received(1).Remove(photo);
        carriers.Received(1).Remove(carrier);

        // One save, so the carrier, its images and the delete instructions
        // commit together or not at all.
        await carriers.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await attachments.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_OfACarrierWithNoImages_QueuesNothing()
    {
        var (carriers, logs, _) = Mocks();
        var carrier = NewCarrier();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.CountForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(0);

        var attachments = Substitute.For<IAttachmentRepository>();
        attachments.ListForCarrierForDeleteAsync(carrier.Id, Arg.Any<CancellationToken>())
                   .Returns([]);

        await DeleteHandler(carriers, logs, attachments)
            .Handle(new DeleteCarrierCommand(Code), default);

        attachments.DidNotReceive().Remove(Arg.Any<Attachment>());
    }

    [Fact]
    public async Task Delete_BlockedByHistory_LeavesImagesAlone()
    {
        var (carriers, logs, _) = Mocks();
        var carrier = NewCarrier();
        carriers.GetByCodeAsync(Code, Arg.Any<CancellationToken>()).Returns(carrier);
        logs.CountForCarrierAsync(carrier.Id, Arg.Any<CancellationToken>()).Returns(2);

        var attachments = Substitute.For<IAttachmentRepository>();

        await DeleteHandler(carriers, logs, attachments)
            .Handle(new DeleteCarrierCommand(Code), default);

        // A refused delete must not queue any bytes for removal — the carrier
        // and its photos are both still in use.
        await attachments.DidNotReceive()
            .ListForCarrierForDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        attachments.DidNotReceive().Remove(Arg.Any<Attachment>());
    }

    private static Attachment PhotoOf(Guid carrierId) => Attachment.For(
        AttachmentOwner.Carrier, carrierId, "dtms-attachments",
        $"carrier/{carrierId}/a.jpg", $"carrier/{carrierId}/a.thumb.jpg",
        "image/jpeg", 2048, "a.jpg", null, "Ada Ops");

    private static DeleteCarrierCommandHandler DeleteHandler(
        ICarrierRepository carriers,
        ICarrierMaintenanceLogRepository logs,
        IAttachmentRepository? attachments = null)
    {
        if (attachments is null)
        {
            attachments = Substitute.For<IAttachmentRepository>();
            attachments.ListForCarrierForDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                       .Returns([]);
        }
        return new DeleteCarrierCommandHandler(carriers, logs, attachments);
    }

    private static (ICarrierRepository, ICarrierMaintenanceLogRepository, ICurrentActorContext) Mocks()
    {
        var actor = Substitute.For<ICurrentActorContext>();
        actor.Current.Returns(new ActorContext { DisplayName = "Ada Ops", PrincipalId = "user:1" });
        return (Substitute.For<ICarrierRepository>(),
                Substitute.For<ICarrierMaintenanceLogRepository>(),
                actor);
    }
}
