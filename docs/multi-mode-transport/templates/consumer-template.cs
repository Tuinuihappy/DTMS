// =============================================================================
// MASSTRANSIT CONSUMER TEMPLATE
// =============================================================================
//
// DTMS uses MassTransit + RabbitMQ for cross-module integration events.
// Consumers handle inbound events from other modules' outbox.
//
// Folder layout:
//   src/Modules/{Module}/.../Application/Consumers/{EventName}Consumer.cs
//
// Reference examples (real working consumers):
//   src/Modules/DeliveryOrder/.../Consumers/TripCompletedConsumer.cs
//     ↑ canonical: cross-module update with idempotency + concurrency handling
//   src/Modules/DeliveryOrder/.../Consumers/TripCancelledConsumer.cs
//   src/Modules/DeliveryOrder/.../Consumers/PodScannedConsumer.cs
//   src/Modules/Planning/.../Consumers/  (event-driven planning)
//
// Critical conventions:
//   1. ALWAYS log inbound event at start with key fields for forensic tracing
//   2. Load related aggregate via repository — if NULL, log + return (idempotent)
//   3. State-machine errors (InvalidOperationException) → LOG + return (no retry)
//      Concurrency errors (DbUpdateConcurrencyException) → THROW (MassTransit retries)
//      Unexpected errors → THROW (MassTransit retries with backoff)
//   4. Idempotency by design: consumer may receive same event multiple times
//      (broker redelivery, deploy overlap). Aggregate's domain methods must be
//      idempotent OR consumer must check state before applying.
//   5. NO side-effects outside the aggregate transaction (no HTTP calls to
//      external systems inside Consume — publish another event for that)
//
// Event versioning: events follow ...IntegrationEventV{N} pattern (per DTMS
// convention). Consumer typically targets specific version; chained consumers
// can convert V1 → V2 if needed.
//
// DELETE THIS COMMENT BLOCK before committing.
// =============================================================================

using DTMS.{Module}.Domain.Entities;
using DTMS.{Module}.Domain.Repositories;
using DTMS.{SourceModule}.IntegrationEvents;       // event being consumed
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DTMS.{Module}.Application.Consumers;

/// <summary>
/// {Purpose in 3-5 sentences:
///  - What event triggers this
///  - What aggregate gets updated as a result
///  - What invariants are preserved
///  - Any non-obvious sequencing (e.g. "must run after PodScannedConsumer because...")}
///
/// Idempotency: {how is duplicate delivery handled? — e.g. "aggregate method
///               is no-op if already in target state"}
/// </summary>
public class {EventName}Consumer : IConsumer<{EventName}IntegrationEvent{Version}>
{
    private readonly I{TargetEntity}Repository _repository;
    private readonly ILogger<{EventName}Consumer> _logger;

    public {EventName}Consumer(
        I{TargetEntity}Repository repository,
        ILogger<{EventName}Consumer> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<{EventName}IntegrationEvent{Version}> context)
    {
        var evt = context.Message;

        // ─── 1. Log inbound for forensic tracing ──────────────────────────
        _logger.LogInformation(
            "Received {EventName} event for {KeyField} {KeyValue}, {SecondaryField} {SecondaryValue}",
            evt.{KeyField}, evt.{SecondaryField});

        // ─── 2. Load related aggregate ────────────────────────────────────
        var aggregate = await _repository.GetByIdAsync(evt.{LookupKey}, context.CancellationToken);
        if (aggregate is null)
        {
            // Idempotent: no aggregate = nothing to do. Common cases:
            //   - Aggregate deleted before this event was processed
            //   - Event belongs to different deployment / replay scenario
            //   - Race: event arrived before aggregate fully persisted (rare)
            _logger.LogWarning(
                "No {TargetEntity} found for {LookupKey} {Value} (Event: {EventField} {EventValue}). Skipping.",
                evt.{LookupKey}, evt.{EventField});
            return;
        }

        // ─── 3. Apply state change ────────────────────────────────────────
        try
        {
            // Domain method should be idempotent OR check state before apply.
            // Example: aggregate.MarkVendorCompleted() with idempotent guard inside

            aggregate.{StateTransitionMethod}(evt.{EventField});

            // Optional: defensive idempotency check at consumer level
            // (use if aggregate method can't be made idempotent)
            //
            // if (aggregate.Status == TargetStatus.{TargetState}) {
            //     _logger.LogInformation("{TargetEntity} {Id} already in target state. Skipping.", aggregate.Id);
            //     return;
            // }
        }
        catch (InvalidOperationException ex)
        {
            // State-machine violation = consumer can't handle this event in
            // current aggregate state. Log + DON'T retry (will keep failing).
            // Investigate logs to find why aggregate is in unexpected state.
            _logger.LogError(ex,
                "Cannot apply {EventName} to {TargetEntity} {Id}: {Message}",
                aggregate.Id, ex.Message);
            return;
        }

        // ─── 4. Persist with concurrency handling ─────────────────────────
        try
        {
            await _repository.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation(
                "{TargetEntity} {Id} updated to {Status} after {EventName}",
                aggregate.Id, aggregate.Status);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two consumers raced on same aggregate. MassTransit will retry
            // — by then the other write will be visible and we either succeed
            // or hit the no-op idempotency guard.
            _logger.LogWarning(
                "Concurrency conflict updating {TargetEntity} {Id} after {EventName}. MassTransit will retry.",
                aggregate.Id);
            throw;
        }
    }
}


// =============================================================================
// COMPANION: Consumer Definition (advanced — optional)
// =============================================================================
//
// Use ConsumerDefinition when you need per-consumer settings (retry policy,
// concurrency limit, prefetch). For default settings, just register the
// consumer and skip this.
//
// public class {EventName}ConsumerDefinition : ConsumerDefinition<{EventName}Consumer>
// {
//     public {EventName}ConsumerDefinition()
//     {
//         Endpoint(e =>
//         {
//             e.PrefetchCount = 16;          // unprocessed messages per consumer
//             e.ConcurrentMessageLimit = 8;  // parallel consume per endpoint
//         });
//     }
//
//     protected override void ConfigureConsumer(
//         IReceiveEndpointConfigurator endpointConfigurator,
//         IConsumerConfigurator<{EventName}Consumer> consumerConfigurator,
//         IRegistrationContext context)
//     {
//         endpointConfigurator.UseMessageRetry(r => r.Exponential(
//             5,                              // max attempts
//             TimeSpan.FromSeconds(1),        // min delay
//             TimeSpan.FromMinutes(5),        // max delay
//             TimeSpan.FromSeconds(2)));      // step
//     }
// }


// =============================================================================
// REGISTRATION (in MassTransit setup — usually Program.cs or module extension)
// =============================================================================

// services.AddMassTransit(x =>
// {
//     x.AddConsumer<{EventName}Consumer>();
//     // ... other consumers
//
//     x.UsingRabbitMq((context, cfg) =>
//     {
//         cfg.Host(connectionString);
//         cfg.ConfigureEndpoints(context);
//     });
// });


// =============================================================================
// TESTING (use unit-test-template.cs pattern + InMemoryTestHarness)
// =============================================================================
//
// MassTransit provides ITestHarness for in-memory consumer testing:
//
// var provider = new ServiceCollection()
//     .AddMassTransitTestHarness(x => x.AddConsumer<{EventName}Consumer>())
//     .AddSingleton<I{TargetEntity}Repository>(fakeRepo)
//     .BuildServiceProvider(true);
//
// var harness = provider.GetRequiredService<ITestHarness>();
// await harness.Start();
//
// await harness.Bus.Publish(new {EventName}IntegrationEvent{Version}(...));
// (await harness.Consumed.Any<{EventName}IntegrationEvent{Version}>()).Should().BeTrue();
//
// For full DB roundtrip use integration-test-template.cs pattern instead.
