using DTMS.Planning.Application.Commands.InstantiateOrderTemplate;
using DTMS.Planning.Application.Services;
using DTMS.Planning.Domain.Entities;
using DTMS.Planning.Domain.Enums;
using DTMS.Planning.Domain.Repositories;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using NSubstitute;

namespace Planning.UnitTests;

// De-duplication of manual OrderTemplate dispatch. RIOT3 does NOT de-duplicate
// on upperKey — a repeated send creates a second real robot order — so every
// decision here has a physical consequence.
//
// Two failure directions matter equally:
//   • dispatching twice for one intent  → two robots run the same job
//   • de-duplicating two intents        → an expected trip silently never runs
// The second is the quieter and more dangerous one, hence the "different key
// always dispatches" tests alongside the "same key never re-dispatches" ones.
public class InstantiateOrderTemplateIdempotencyTests
{
    private const string Key = "intent-key-1";

    [Fact]
    public async Task FirstDispatch_ClaimsKeyAndSendsOnce()
    {
        var h = new Harness();
        var result = await h.Dispatch(Key);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Replayed.Should().BeFalse();
        await h.Dispatcher.Received(1).SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameKeyAfterSuccess_ReplaysWithoutSendingAgain()
    {
        var h = new Harness();
        var first = await h.Dispatch(Key);

        // Second call with the same intent: the claim is already Succeeded.
        var second = await h.Dispatch(Key);

        second.IsSuccess.Should().BeTrue();
        second.Value!.Replayed.Should().BeTrue("the stored outcome is returned, not a new dispatch");
        second.Value.UpperKey.Should().Be(first.Value!.UpperKey);
        await h.Dispatcher.Received(1).SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DifferentKeys_BothDispatch()
    {
        // Firing the same template several times in a row is normal operation
        // (e.g. three trips of one route) and must never be swallowed.
        var h = new Harness();

        await h.Dispatch("intent-1");
        await h.Dispatch("intent-2");
        await h.Dispatch("intent-3");

        await h.Dispatcher.Received(3).SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DifferentKey_DispatchesEvenWhileEarlierAttemptIsUnresolved()
    {
        // An unresolved previous attempt must not block a brand-new intent —
        // the guard is scoped to the key, never to the template.
        var h = new Harness();
        h.SeedClaim("stuck-intent", DispatchClaimStatus.InProgress, h.HashFor());

        var result = await h.Dispatch("fresh-intent");

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameKeyDifferentBody_IsRejected()
    {
        var h = new Harness();
        await h.Dispatch(Key, priority: 5);

        var conflicting = await h.Dispatch(Key, priority: 9);

        conflicting.IsSuccess.Should().BeFalse();
        conflicting.Error.Should().Contain(InstantiateFailureCodes.BodyMismatch);
        await h.Dispatcher.Received(1).SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InDoubt_VendorHasOrder_AdoptsItInsteadOfResending()
    {
        var h = new Harness();
        h.SeedClaim(Key, DispatchClaimStatus.InProgress, h.HashFor());
        h.StatusQuery.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RobotOrderPresence.Exists);

        var result = await h.Dispatch(Key);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Replayed.Should().BeTrue();
        await h.Dispatcher.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InDoubt_VendorConfirmsNothingCreated_Retries()
    {
        var h = new Harness();
        h.SeedClaim(Key, DispatchClaimStatus.InProgress, h.HashFor());
        h.StatusQuery.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RobotOrderPresence.NotFound);

        var result = await h.Dispatch(Key);

        result.IsSuccess.Should().BeTrue();
        await h.Dispatcher.Received(1).SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InDoubt_VendorUnreachable_BlocksInsteadOfGuessing()
    {
        // Cannot tell whether an order exists. Blocking costs the operator a
        // retry; guessing "resend" costs a duplicate robot movement.
        var h = new Harness();
        h.SeedClaim(Key, DispatchClaimStatus.InProgress, h.HashFor());
        h.StatusQuery.CheckAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(RobotOrderPresence.Unknown);

        var result = await h.Dispatch(Key);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain(InstantiateFailureCodes.InProgress);
        await h.Dispatcher.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VendorRejects_MarksFailedSoTheSameKeyCanBeRetried()
    {
        var h = new Harness();
        h.Dispatcher.SendAsync(Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>())
            .Returns(Result<RobotOrderDispatchResult>.Failure("vendor said no"));

        var failed = await h.Dispatch(Key);
        failed.IsSuccess.Should().BeFalse();
        h.Claims.Single().Status.Should().Be(DispatchClaimStatus.Failed);

        // Same key again: nothing was created vendor-side, so it may re-drive.
        h.Dispatcher.SendAsync(Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>())
            .Returns(Result<RobotOrderDispatchResult>.Success(new RobotOrderDispatchResult("vk-2", "{}")));

        var retried = await h.Dispatch(Key);
        retried.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchWithoutKey_IsRefused()
    {
        var h = new Harness();
        var result = await h.Dispatch(idempotencyKey: null);

        result.IsSuccess.Should().BeFalse();
        await h.Dispatcher.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DryRun_NeedsNoKeyAndConsumesNoClaim()
    {
        var h = new Harness();
        var result = await h.Dispatch(idempotencyKey: null, dryRun: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DryRun.Should().BeTrue();
        h.Claims.Should().BeEmpty();
        await h.Dispatcher.DidNotReceive().SendAsync(
            Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>());
    }

    // ── Harness ─────────────────────────────────────────────────────────
    // Fake claim repository backed by a list, mirroring the real unique
    // index: a second insert of the same key loses and returns null.
    private sealed class Harness
    {
        public readonly List<DispatchClaim> Claims = new();
        public readonly IRobotOrderDispatcher Dispatcher = Substitute.For<IRobotOrderDispatcher>();
        public readonly IRobotOrderStatusQuery StatusQuery = Substitute.For<IRobotOrderStatusQuery>();
        private readonly OrderTemplate _template;
        private readonly InstantiateOrderTemplateCommandHandler _handler;

        public Harness()
        {
            _template = new OrderTemplate(
                name: "SHELF1 TO STF_02",
                priority: 50,
                structureType: "sequence",
                transportOrderPriority: 50,
                missions: new[] { OrderTemplateMission.CreateMove(1, "agv", 2, 179) });

            var templates = Substitute.For<IOrderTemplateRepository>();
            templates.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_template);

            var resolver = Substitute.For<IOrderTemplateResolver>();
            resolver.ResolveAsync(Arg.Any<OrderTemplate>(), Arg.Any<CancellationToken>())
                .Returns(new ResolvedOrder(
                    _template.Name, 50, "sequence", 50,
                    Array.Empty<ResolvedMission>(), null, null, null, null, null));

            Dispatcher.SendAsync(Arg.Any<string>(), Arg.Any<ResolvedOrder>(), Arg.Any<CancellationToken>())
                .Returns(Result<RobotOrderDispatchResult>.Success(new RobotOrderDispatchResult("vk-1", "{}")));

            var claims = Substitute.For<IDispatchClaimRepository>();
            claims.TryClaimAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var key = ci.ArgAt<string>(0);
                    if (Claims.Any(c => c.IdempotencyKey == key)) return null; // unique violation
                    var claim = new DispatchClaim(key, ci.ArgAt<Guid>(1), ci.ArgAt<string>(2));
                    Claims.Add(claim);
                    return claim;
                });
            claims.GetByKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => Claims.FirstOrDefault(c => c.IdempotencyKey == ci.ArgAt<string>(0)));

            _handler = new InstantiateOrderTemplateCommandHandler(
                templates, claims, resolver, Dispatcher, StatusQuery);
        }

        public string HashFor() => "seeded-hash-placeholder";

        // Seeds a claim whose RequestHash matches whatever the handler will
        // compute for the default command, so hash comparison passes and the
        // test exercises the status branch instead.
        public void SeedClaim(string key, DispatchClaimStatus status, string _)
        {
            var claim = new DispatchClaim(key, _template.Id, ComputeDefaultHash());
            if (status == DispatchClaimStatus.Failed) claim.MarkFailed("seeded");
            else if (status == DispatchClaimStatus.Succeeded) claim.MarkSucceeded("seeded-vk");
            Claims.Add(claim);
        }

        // Mirrors the handler's canonical hash for a command with no overrides.
        private string ComputeDefaultHash()
        {
            var canonical = System.Text.Json.JsonSerializer.Serialize(new
            {
                OrderTemplateId = _template.Id,
                PriorityOverride = (int?)null,
                AppointVehicleKeyOverride = (string?)null,
                AppointVehicleNameOverride = (string?)null,
                AppointVehicleGroupKeyOverride = (string?)null,
                AppointVehicleGroupNameOverride = (string?)null,
                AppointQueueWaitAreaOverride = (string?)null
            });
            return Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(canonical)))[..64].ToLowerInvariant();
        }

        public Task<Result<InstantiateOrderTemplateResult>> Dispatch(
            string? idempotencyKey,
            int? priority = null,
            bool dryRun = false)
            => _handler.Handle(
                new InstantiateOrderTemplateCommand(
                    OrderTemplateId: _template.Id,
                    PriorityOverride: priority,
                    DryRun: dryRun,
                    IdempotencyKey: idempotencyKey),
                CancellationToken.None);
    }
}
