using DTMS.DeliveryOrder.Infrastructure.Data;
using DTMS.Dispatch.Infrastructure.Data;
using DTMS.Fleet.Infrastructure.Data;
using DTMS.Planning.Infrastructure.Data;
using DTMS.SharedKernel.Outbox;
using DTMS.Transport.Amr.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DTMS.Api.Infrastructure.Outbox;

/// <summary>
/// Phase O3 — routes DLQ replay back to the module that owns the
/// original OutboxMessages table. Switch on <c>Source</c> string.
/// Keep this in sync with the module set OutboxProcessorService drains
/// (see <c>ProcessUnpublishedEventsAsync</c>'s modules array).
/// </summary>
public sealed class DeadLetterReplayRouter : IDeadLetterReplayRouter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DeadLetterReplayRouter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task ReinsertAsync(string source, OutboxMessage message, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        DbContext db = source switch
        {
            DeliveryOrderDbContext.Schema  => scope.ServiceProvider.GetRequiredService<DeliveryOrderDbContext>(),
            PlanningDbContext.Schema       => scope.ServiceProvider.GetRequiredService<PlanningDbContext>(),
            DispatchDbContext.Schema       => scope.ServiceProvider.GetRequiredService<DispatchDbContext>(),
            FleetDbContext.Schema          => scope.ServiceProvider.GetRequiredService<FleetDbContext>(),
            VendorAdapterDbContext.Schema  => scope.ServiceProvider.GetRequiredService<VendorAdapterDbContext>(),
            _ => throw new InvalidOperationException(
                $"Unknown DLQ source '{source}' — cannot route replay. Add the schema to DeadLetterReplayRouter."),
        };

        db.Set<OutboxMessage>().Add(message);
        await db.SaveChangesAsync(ct);
    }
}
