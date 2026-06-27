# Integration test debt — 21 pre-existing failures

> **Use as a GitHub issue body**: copy this file's content (below the title row) into a new issue on `github.com/Tuinuihappy/DTMS/issues/new`. Title: `chore(tests): triage 21 pre-existing integration test failures`.

## Background

The `DTMS.IntegrationTests` project had a long-standing compile error (missing `NSubstitute` reference + a constructor-signature mismatch in `DomainEventMapperTests`) that blocked the whole project from building. Because nothing compiled, **none of these tests ever ran in CI**.

Fixed during T1 work (commits [`5119e3f`](https://github.com/Tuinuihappy/DTMS/commit/5119e3f) and [`92df8ab`](https://github.com/Tuinuihappy/DTMS/commit/92df8ab)):
- Added `NSubstitute` to the test csproj
- Updated `DomainEventMapperTests` constructors to pass the now-required `ICurrentActorContext`
- Added `[assembly: CollectionBehavior(DisableTestParallelization = true)]` so Testcontainers PostgreSQL instances don't collide across classes
- Bumped 4 stale `SchemaVersion = "1.0"` assertions to `"1.1"` in `DomainEventMapperTests` (commit [`b4f4ef1`](https://github.com/Tuinuihappy/DTMS/commit/b4f4ef1))

That left **21 pre-existing failures visible** — all of them assertion-drift or genuine logic regressions that have been silently broken behind the compile-error wall, plausibly for many months.

## Scoreboard

| Status | Count |
|---|---|
| Pass | 47 |
| **Fail** | **21** |
| Skipped | 15 |
| Total | 83 |

## Categorisation (21 failures across 7 classes)

Each class likely has a single root cause shared by its failures. Triage by class, not by individual test.

### 1. `OutboxTests` — 5 failures (~2-3h to fix)

```
PublishEvent_AfterWebhookCall_RowExistsInOutbox
OutboxMessage_ProcessedOnUtc_SetAfterProcessorRuns
OutboxMessage_Content_IsValidJsonWithEventData
OutboxMessage_Type_ContainsFullyQualifiedEventName
MultipleEvents_FromSameTrigger_AllWrittenToOutbox
```
**Likely root cause**: Outbox row schema / payload format changed since these tests were written (multiple schemas now exist — `outbox.OutboxMessages` + per-module variants). Tests may be querying the wrong schema or asserting an older JSON shape.

**Owner**: Infra / SharedKernel.Outbox

### 2. `Riot3WebhookTests` — 5 failures (~3-4h)

```
Notify_TaskFinished_WritesCompletedEventToOutbox
Notify_TaskFailed_WritesFailedEventToOutbox
Notify_TaskNotifyAlias_WritesCompletedEventToOutbox
Notify_VehicleNotifyAliasWithExecutingState_MapsToMovingState
Notify_VehicleEventWithVendorDeviceKey_MapsToAppVehicleId
```
**Likely root cause**: RIOT3 webhook payload shape or state-machine mappings changed (Phase b9 introduced TripFailed/Cancelled webhooks, vendor state codes evolved).

**Owner**: VendorAdapter / Riot3

### 3. `EndToEndFlowTests` — 3 failures (~4-6h)

```
ProtectedEndpoint_WithoutToken_Returns401
Auth_LoginWithValidCredentials_ReturnsToken
Dispatch_TripLifecycle_CreateStartComplete
Dispatch_CreateTrip_FullFlow
```
**Likely root cause**: Auth flow refactored (Auth__Disable in dev, JWT issuance) and Trip API contract changed (envelope dispatch added in Phase b7).

**Owner**: Backend / Auth + Dispatch

### 4. `EndToEndPipelineTests` — 2 failures (~3-4h)

```
Pipeline_FullFlow_TripReachesCompletedStatus
Pipeline_TripCompleted_WritesToOutbox
```
**Likely root cause**: Either the same Trip contract changes as `EndToEndFlowTests`, or Trip completion logic changed (item-level Delivered transitions added).

**Owner**: Backend

### 5. `AmendmentTimelineTests` — 3 failures (~2-3h)

```
PatchOrder_ChangeServiceWindow_TimelineIncludesAmendmentEntry
PatchOrder_ChangeServiceWindow_TimelineIncludesAmendment
PatchOrder_MultipleAmendments_TimelineOrderedChronologically
```
**Likely root cause**: Amendment timeline projection format changed (OrderActivityProjector evolution).

**Owner**: DeliveryOrder

### 6. `ChargingPolicyTests` — 3 failures (~2-3h)

```
VehicleWebhook_BatteryBelowThreshold_WritesBatteryLowEventToOutbox
VehicleWebhook_BatteryExactly19_WritesBatteryLowEvent
VehicleWebhook_AlsoWritesStateChangedEvent
```
**Likely root cause**: Battery threshold logic moved (charging policy is dynamic now), or vehicle webhook payload format drift.

**Owner**: Fleet

## Recommendation

- **Don't block CI on these.** Either skip pre-existing failures with `[Fact(Skip = "pre-existing — see #<this-issue>")]`, or split them into a dedicated test job that's allowed to fail until triage is complete.
- **Triage by class, in priority order**: `EndToEndFlowTests` + `EndToEndPipelineTests` first (they're the "smoke tests" — broken E2E flows are scariest), then `OutboxTests` (infra correctness), then the rest.
- **Each class is one PR.** Don't bundle.

## Verification commands

To reproduce locally:

```bash
# Requires Docker running (Testcontainers PostgreSQL)
dotnet build tests/Integration/DTMS.IntegrationTests/DTMS.IntegrationTests.csproj
dotnet test  tests/Integration/DTMS.IntegrationTests/DTMS.IntegrationTests.csproj

# Run a single class to triage
dotnet test  tests/Integration/DTMS.IntegrationTests/DTMS.IntegrationTests.csproj \
  --filter "FullyQualifiedName~OutboxTests"
```

## Out of scope

- T1 crash-recovery tests in this project (`T1_DeliveryOrderValidatedConsumerIntegrationTests`) — those **pass**.
- 15 skipped tests (unrelated — pre-existing `[Skip]` attributes).

---

*Triage filed as part of the T1 crash-recovery rollout — see [`docs/crash-recovery-workflow-resilience-plan.md`](crash-recovery-workflow-resilience-plan.md) for context.*
