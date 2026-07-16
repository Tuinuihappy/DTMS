using DTMS.Api.Infrastructure.Outbox;
using DTMS.SharedKernel.Outbox;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DTMS.Api.UnitTests;

// Latent gap fixed 2026-07-16: the central pass DLQs rows with
// Source = "outbox" (OutboxDbContext.Schema) since it started draining the
// central table, but the replay router only knew the 5 module schemas —
// so POST /admin/outbox/{id}/replay threw "Unknown DLQ source 'outbox'"
// for exactly those rows. These tests pin the new case + the guard.
public class DeadLetterReplayRouterTests
{
    private static (DeadLetterReplayRouter Router, ServiceProvider Provider) NewRouter()
    {
        var services = new ServiceCollection();
        // Fix the database name outside the options lambda — the lambda runs
        // per context creation, so an inline Guid would give every scope its
        // own empty database.
        var dbName = "dlq-replay-" + Guid.NewGuid();
        services.AddDbContext<OutboxDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        return (new DeadLetterReplayRouter(provider.GetRequiredService<IServiceScopeFactory>()), provider);
    }

    private static OutboxMessage Replay() => new(
        id: Guid.NewGuid(),
        type: "DTMS.DeliveryOrder.IntegrationEvents.SourceCallbackOutcome",
        content: "{\"success\":false}",
        occurredOnUtc: DateTime.UtcNow);

    [Fact]
    public async Task ReinsertAsync_OutboxSource_LandsInCentralTable()
    {
        var (router, provider) = NewRouter();
        var replay = Replay();

        await router.ReinsertAsync(OutboxDbContext.Schema, replay);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        var row = await db.OutboxMessages.SingleAsync(m => m.Id == replay.Id);
        row.Type.Should().Be(replay.Type);
        row.ProcessedOnUtc.Should().BeNull("a replayed row must be pending again");
    }

    [Fact]
    public async Task ReinsertAsync_UnknownSource_StillThrows()
    {
        var (router, _) = NewRouter();

        var act = () => router.ReinsertAsync("no-such-schema", Replay());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown DLQ source 'no-such-schema'*");
    }
}
