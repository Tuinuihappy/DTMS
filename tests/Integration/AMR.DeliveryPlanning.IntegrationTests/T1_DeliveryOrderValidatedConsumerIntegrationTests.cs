using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.DeliveryOrder.IntegrationEvents;
using DTMS.Dispatch.Infrastructure.Data;
using AMR.DeliveryPlanning.Planning.Application.Consumers;
using AMR.DeliveryPlanning.Planning.Application.Services;
using AMR.DeliveryPlanning.Planning.Domain.Enums;
using AMR.DeliveryPlanning.Planning.Infrastructure.Data;
using DTMS.SharedKernel.Diagnostics;
using DTMS.SharedKernel.Messaging;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Net.Http.Json;

namespace AMR.DeliveryPlanning.IntegrationTests;

/// <summary>
/// T1.2 — end-to-end integration test for DeliveryOrderValidatedConsumer.
/// Exercises the auto-planning pipeline against a real PostgreSQL database
/// (Testcontainers) and real MediatR handlers across three modules
/// (DeliveryOrder, Planning, Dispatch). The only thing mocked is the vendor
/// seam (IDispatchOrderTemplateService) so we can inject success / failure /
/// throw / orphan scenarios that production rarely surfaces.
///
/// Scenarios mirror the failure modes that T1 was built to recover from:
/// the OD-0374 / OD-0375 incident shape (consumer crashed between
/// MarkOrderPlanned and DispatchByRouteAsync) corresponds to the
/// "vendor throws" path here.
/// </summary>
public class T1_DeliveryOrderValidatedConsumerIntegrationTests : IClassFixture<DtmsWebApplicationFactory>
{
    private readonly DtmsWebApplicationFactory _factory;

    public T1_DeliveryOrderValidatedConsumerIntegrationTests(DtmsWebApplicationFactory factory)
        => _factory = factory;

    // ─── Scenario 1: happy path single group ──────────────────────────────

    [Fact]
    public async Task Consume_HappyPath_OrderReachesDispatchedAndJobBoundToTrip()
    {
        var ctx = await SetupConfirmedOrderAsync(itemCount: 2);
        var fakeTripId = Guid.NewGuid();
        var vendor = Substitute.For<IDispatchOrderTemplateService>();
        vendor.DispatchByRouteAsync(
                ctx.OrderId, ctx.PickupId, ctx.DropId,
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Guid?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result<DispatchTemplateResult>.Success(
                BuildDispatchResult(fakeTripId, vendorKey: "VK-001")));

        await InvokeConsumerAsync(ctx, vendor);

        var (order, jobs) = await ReadStateAsync(ctx);
        order.Status.Should().Be(
            DTMS.DeliveryOrder.Domain.Enums.OrderStatus.Dispatched,
            "single group dispatched successfully → Order advances to Dispatched");
        jobs.Should().ContainSingle();
        jobs[0].Status.Should().Be(JobStatus.Dispatched);
        jobs[0].TripId.Should().Be(fakeTripId);
        jobs[0].VendorOrderKey.Should().Be("VK-001");
    }

    // ─── Scenario 2: vendor throws → group failed, others continue ────────

    [Fact]
    public async Task Consume_VendorThrows_JobMarkedFailedWithDispatchException_NoTripCreated()
    {
        var ctx = await SetupConfirmedOrderAsync(itemCount: 1);
        var vendor = Substitute.For<IDispatchOrderTemplateService>();
        vendor.DispatchByRouteAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Guid?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("vendor network outage"));

        // The pre-T1.2 bug shape: this consume would have left the Job at
        // Created with no Trip and the order stuck Planned. Now it must mark
        // the Job Failed (so the order can converge) and NOT throw out of
        // the consumer — other groups (or future events) get a chance.
        await InvokeConsumerAsync(ctx, vendor);

        var (order, jobs) = await ReadStateAsync(ctx);
        jobs.Should().ContainSingle();
        jobs[0].Status.Should().Be(JobStatus.Failed,
            "T1.2 catch should have marked the Job Failed");
        jobs[0].FailureCategory.Should().Be(JobFailureCategory.DispatchException);
        jobs[0].FailureReason.Should().Contain("InvalidOperationException");

        // Order status: with all groups failed and successCount=0, the consumer
        // sends RecomputeOrderStatusCommand which converges from item states.
        // Items in the failed group were marked dispatch-failed → order moves
        // off "stuck Planned" toward Failed or PartiallyCompleted.
        order.Status.Should().NotBe(
            DTMS.DeliveryOrder.Domain.Enums.OrderStatus.Planned,
            "the whole point of T1.2 is the order is no longer stuck at Planned after a vendor throw");
    }

    // ─── Scenario 3: redelivery / idempotency ─────────────────────────────

    [Fact]
    public async Task Consume_SameEventTwice_NoDuplicateJobAndTripIdStable()
    {
        // MassTransit retry / watchdog replay means the same event can arrive
        // twice. T1.5 guards must dedup so we end up with exactly one Job
        // bound to exactly one TripId — never two anchors per group nor a
        // job repointed at a different Trip.
        var ctx = await SetupConfirmedOrderAsync(itemCount: 1);
        var stableTripId = Guid.NewGuid();
        var vendor = Substitute.For<IDispatchOrderTemplateService>();
        vendor.DispatchByRouteAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Guid?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result<DispatchTemplateResult>.Success(
                BuildDispatchResult(stableTripId, vendorKey: "VK-IDEMP")));

        await InvokeConsumerAsync(ctx, vendor);   // first delivery
        await InvokeConsumerAsync(ctx, vendor);   // redelivery

        var (order, jobs) = await ReadStateAsync(ctx);
        jobs.Should().ContainSingle("T1.5 CreateJobAnchor idempotency — no duplicate Job per (Order, Group)");
        jobs[0].TripId.Should().Be(stableTripId,
            "T1.5 MarkJobDispatched idempotency — same TripId is a no-op, never repoint");
        order.Status.Should().Be(
            DTMS.DeliveryOrder.Domain.Enums.OrderStatus.Dispatched);
    }

    // ─── Scenario 4: multi-group with mixed success ───────────────────────

    [Fact]
    public async Task Consume_TwoGroupsOneSucceedsOneFails_OrderDispatchedAndFailedGroupItemsMarked()
    {
        // Two items, two distinct routes → two station groups → two parallel
        // dispatches. T1.2's per-group try/catch must let group A complete
        // even when group B's vendor call throws. Without it, the loop would
        // have aborted and group A's items would silently stay Pending.
        var (pickupB, dropB) = await CreateSecondaryStationPairAsync();
        var ctx = await SetupConfirmedOrderAsync(itemCount: 1);
        var twoRoutes = new[]
        {
            (ctx.PickupId, ctx.DropId),
            (pickupB,      dropB)
        };

        var fakeTripA = Guid.NewGuid();
        var vendor = Substitute.For<IDispatchOrderTemplateService>();
        vendor.DispatchByRouteAsync(
                Arg.Any<Guid>(), ctx.PickupId, ctx.DropId,
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Guid?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result<DispatchTemplateResult>.Success(
                BuildDispatchResult(fakeTripA, vendorKey: "VK-A")));
        vendor.DispatchByRouteAsync(
                Arg.Any<Guid>(), pickupB, dropB,
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Guid?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("group B vendor failed"));

        await InvokeConsumerAsync(ctx, vendor, routes: twoRoutes);

        var (order, jobs) = await ReadStateAsync(ctx);
        jobs.Should().HaveCount(2);
        jobs.Should().ContainSingle(j => j.Status == JobStatus.Dispatched
                                    && j.TripId == fakeTripA);
        jobs.Should().ContainSingle(j => j.Status == JobStatus.Failed
                                    && j.FailureCategory == JobFailureCategory.DispatchException);

        // successCount >= 1 → Order should reach Dispatched. The failed group's
        // items are marked Failed; the successful group's items carry on.
        order.Status.Should().Be(
            DTMS.DeliveryOrder.Domain.Enums.OrderStatus.Dispatched,
            "at least one group succeeded → order advances despite the other failing");
    }

    // ─── Scenario 5: OperationCanceledException rethrows ───────────────────

    [Fact]
    public async Task Consume_VendorObservesCancellation_RethrowsForMassTransitRedelivery()
    {
        // T1.3 graceful shutdown signals the host stopping by cancelling the
        // consumer's CancellationToken. The consumer must rethrow OCE so
        // MassTransit redelivers the message — swallowing it would leave the
        // group's Job at Created and the order Planned (the original bug).
        var ctx = await SetupConfirmedOrderAsync(itemCount: 1);
        using var cts = new CancellationTokenSource();
        var vendor = Substitute.For<IDispatchOrderTemplateService>();
        vendor.DispatchByRouteAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Guid?>(),
                Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(_ =>
            {
                cts.Cancel();   // simulate "host is shutting down"
                return new OperationCanceledException(cts.Token);
            });

        var act = async () => await InvokeConsumerAsync(ctx, vendor, token: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "T1.2 explicitly rethrows OCE so MassTransit redelivers on shutdown");

        var (_, jobs) = await ReadStateAsync(ctx);
        jobs.Should().ContainSingle(j => j.Status == JobStatus.Created,
            "the Job anchor was created in Phase 1b but the dispatch step never completed — " +
            "exactly the redeliverable state we want the watchdog (T1.4) to recover");
    }

    // Creates an additional pickup+drop pair on a separate facility map so
    // multi-group scenarios can simulate distinct routes within one order.
    private async Task<(Guid pickup, Guid drop)> CreateSecondaryStationPairAsync()
    {
        var client = await _factory.GetAuthenticatedClient();
        return await _factory.CreateStationPairAsync(client);
    }

    // ─── Test fixture helpers ──────────────────────────────────────────────

    /// <summary>
    /// Wires up a Confirmed-status DeliveryOrder via the real HTTP /upstream
    /// path so we exercise the full creation pipeline (CreateFromUpstream +
    /// MarkAsValidated + Confirm in one transaction). Returns everything the
    /// consumer needs to be invoked manually: order id + the station ids the
    /// integration event will reference.
    /// </summary>
    private async Task<TestContext> SetupConfirmedOrderAsync(int itemCount = 1)
    {
        var client = await _factory.GetAuthenticatedClient();
        var (pickupId, dropId) = await _factory.CreateStationPairAsync(client);
        var profileCode = await _factory.CreateLoadUnitProfileAsync(client);

        var items = new List<object>();
        for (var i = 1; i <= itemCount; i++)
        {
            items.Add(new
            {
                ItemId = $"ITEM-{Guid.NewGuid():N}"[..15],
                PickupLocationCode = pickupId.ToString(),
                DropLocationCode = dropId.ToString(),
                LoadUnitProfileCode = profileCode,
                WeightKg = 5.0,
                Quantity = new { Value = 1.0, Uom = "BOX" }
            });
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/delivery-orders/upstream")
        {
            Content = JsonContent.Create(new
            {
                SourceSystem = "OMS",
                OrderRef = $"T1C-{Guid.NewGuid():N}"[..15],
                Priority = "Normal",
                RequestedTransportMode = "AMR",
                ServiceWindow = new
                {
                    EarliestUtc = (DateTime?)null,
                    LatestUtc = DateTime.UtcNow.AddHours(4)
                },
                Items = items
            })
        };
        request.Headers.Add("Idempotency-Key", "upstream-" + Guid.NewGuid());
        var resp = await client.SendAsync(request);

        resp.IsSuccessStatusCode.Should().BeTrue(
            $"upstream ingest failed: {await resp.Content.ReadAsStringAsync()}");

        var ack = await resp.Content.ReadFromJsonAsync<UpstreamAckDto>();
        ack.Should().NotBeNull();
        return new TestContext(ack!.Order.Id, pickupId, dropId, profileCode, itemCount);
    }

    /// <summary>
    /// Build the integration event the consumer expects, with items that
    /// correspond to the order's actual items + station mapping. In production
    /// this event is published by the outbox after Confirm fires the domain
    /// event; here we build it by hand so we can inject it directly.
    /// </summary>
    private static DeliveryOrderConfirmedIntegrationEventV1 BuildEvent(
        TestContext ctx, IEnumerable<(Guid pickup, Guid drop)>? overrideRoutes = null)
    {
        var routes = overrideRoutes?.ToList()
                     ?? Enumerable.Range(0, ctx.ItemCount).Select(_ => (ctx.PickupId, ctx.DropId)).ToList();

        var items = routes.Select((r, idx) => new ItemSummaryDto(
            ItemId: $"ITEM-EVT-{idx}",
            WeightKg: 5.0,
            PickupStationId: r.pickup,
            DropStationId: r.drop)).ToList();

        return new DeliveryOrderConfirmedIntegrationEventV1(
            EventId: Guid.NewGuid(),
            OccurredOn: DateTime.UtcNow,
            DeliveryOrderId: ctx.OrderId,
            Priority: "Normal",
            EarliestUtc: null,
            LatestUtc: DateTime.UtcNow.AddHours(4),
            SubmittedAt: DateTime.UtcNow,
            Items: items,
            RequestedTransportMode: "AMR");
    }

    /// <summary>
    /// Hand-instantiate the consumer with a real ISender (from DI, so all
    /// downstream MediatR handlers and their EF interactions are real) but a
    /// supplied mock for the vendor seam. The trick that makes this test
    /// useful: we never start MassTransit so RabbitMQ isn't needed — the
    /// consumer is just a class with a Consume() method and we call it.
    /// </summary>
    private async Task InvokeConsumerAsync(
        TestContext ctx,
        IDispatchOrderTemplateService vendor,
        IEnumerable<(Guid pickup, Guid drop)>? routes = null,
        CancellationToken token = default)
    {
        using var scope = _factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var metrics = scope.ServiceProvider.GetRequiredService<WorkflowMetrics>();

        var consumer = new DeliveryOrderValidatedConsumer(
            vendor, sender, metrics, NullLogger<DeliveryOrderValidatedConsumer>.Instance);

        var evt = BuildEvent(ctx, routes);
        var consumeCtx = Substitute.For<ConsumeContext<DeliveryOrderConfirmedIntegrationEventV1>>();
        consumeCtx.Message.Returns(evt);
        consumeCtx.CancellationToken.Returns(token);

        await consumer.Consume(consumeCtx);
    }

    /// <summary>
    /// Snapshot the write-side state after Consume returns. Both DbContexts
    /// use fresh scopes so we see committed values not change-tracker
    /// in-memory snapshots from inside the consumer's scope.
    /// </summary>
    private async Task<(DTMS.DeliveryOrder.Domain.Entities.DeliveryOrder Order,
                       List<AMR.DeliveryPlanning.Planning.Domain.Entities.Job> Jobs)>
        ReadStateAsync(TestContext ctx)
    {
        using var scope = _factory.Services.CreateScope();
        var doDb = scope.ServiceProvider.GetRequiredService<DeliveryOrderDbContext>();
        var planDb = scope.ServiceProvider.GetRequiredService<PlanningDbContext>();

        var order = await doDb.DeliveryOrders
            .Include(o => o.Items)
            .FirstAsync(o => o.Id == ctx.OrderId);

        var jobs = await planDb.Jobs
            .Where(j => j.DeliveryOrderId == ctx.OrderId)
            .OrderBy(j => j.GroupIndex)
            .ToListAsync();

        return (order, jobs);
    }

    private static DispatchTemplateResult BuildDispatchResult(Guid tripId, string vendorKey)
    {
        var resolved = new ResolvedOrder(
            Name: "test-order",
            Priority: 1,
            StructureType: "sequence",
            TransportOrderPriority: 1,
            Missions: Array.Empty<ResolvedMission>(),
            AppointVehicleKey: null,
            AppointVehicleName: null,
            AppointVehicleGroupKey: null,
            AppointVehicleGroupName: null,
            AppointQueueWaitArea: null);

        return new DispatchTemplateResult(
            OrderTemplateId: Guid.NewGuid(),
            TemplateName: "TestTemplate",
            VendorOrderKey: vendorKey,
            TripId: tripId,
            Resolved: resolved);
    }

    private sealed record TestContext(
        Guid OrderId, Guid PickupId, Guid DropId, string ProfileCode, int ItemCount);

    private sealed record UpstreamAckDto(UpstreamOrderDto Order);
    private sealed record UpstreamOrderDto(Guid Id);
}
