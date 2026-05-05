# RIOT3 Webhook Events — Implementation Plan

**Scope:** เพิ่ม event handling ที่ยังขาดอยู่ใน RIOT3 webhook pipeline  
**Status:** Pending  
**Dependencies:** `MakeTrip_VehicleId_Nullable` migration (completed)

---

## Background

ปัจจุบัน webhook `/api/webhooks/riot3/notify` รองรับแค่ `finished` และ `failed` events ส่วน events อื่นถูก log ทิ้งหรือไม่ได้ handle เลย Plan นี้ครอบคลุม 3 กลุ่ม:

| กลุ่ม | Events ที่ขาด |
|---|---|
| Task (`type="task"`) | `TASK_PROCESSING`, `TASK_CANCELED`, `TASK_HANG`, `TASK_HELD`, `TASK_HANG_TO_CONTINUE`, `TASK_HELD_TO_CONTINUE` |
| Sub-task (`type="subtask"`) | `SUB_TASK_PROCESSING` |
| Vehicle (`type="vehicle"`) | `VEHICLE_ENTER_EMERGENCY` |

### Flow หลัก (เหมือนกันทุก event)

```
Riot3Webhooks.cs (VendorAdapter)
  → publish IntegrationEvent via IVendorAdapterOutbox
  → MassTransit Consumer (Dispatch.Application)
  → MediatR Command + Handler
  → Domain method on Trip / RobotTask
```

RIOT3-specific concepts (deviceKey, event type strings) ถูก resolve และแปลในชั้น VendorAdapter เสมอ — Dispatch รับแค่ internal ID และ domain-neutral commands

---

## Dependency Order

```
Step 1  Riot3NotifyPayload — extend model
Step 2  Trip.AssignVehicle() + TripVehicleAssignedDomainEvent
Step 3  Integration Events (7 events)
  └─ ต้องทำก่อน Step 4 และ Step 6
Step 4  Webhook handler cases
  └─ depends on Step 1, Step 3
Step 5  Commands: AssignTripVehicle, MarkSubTaskInProgress
  └─ depends on Step 2
Step 6  Consumers (7 consumers)
  └─ depends on Step 3, Step 5
Step 7  TripRepository — VehicleId tracking + GetActiveTripsByVehicleIgnoringTenantAsync
  └─ ทำพร้อม Step 5 ได้
```

**ลำดับ commit ที่แนะนำ:** Steps 1+2 → Steps 3+4 → Steps 5+7 → Step 6

---

## Step 1 — Extend `Riot3NotifyPayload`

**File:** `src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Riot3/Models/Riot3NotifyPayload.cs`

เพิ่ม property `Task` เพื่อรับ nested object จาก RIOT3 ที่มี `processingVehicle`, `cancelReason`, `hangReason`:

```csharp
// เพิ่มใน Riot3NotifyPayload
[JsonPropertyName("task")]
public Riot3TaskInfo? Task { get; set; }

// New supporting classes
public class Riot3TaskInfo
{
    [JsonPropertyName("upperKey")]
    public string? UpperKey { get; set; }       // = our TaskId (Guid)

    [JsonPropertyName("key")]
    public string? Key { get; set; }            // RIOT3 internal order key

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("processingVehicle")]
    public Riot3ProcessingVehicle? ProcessingVehicle { get; set; }

    [JsonPropertyName("cancelReason")]
    public string? CancelReason { get; set; }

    [JsonPropertyName("hangReason")]
    public string? HangReason { get; set; }
}

public class Riot3ProcessingVehicle
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;   // RIOT3 deviceKey
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
```

> **Note:** `payload.UpperKey` (top-level) ยังคงใช้ได้เป็น fallback  
> Priority: `payload.Task?.UpperKey ?? payload.UpperKey`

---

## Step 2 — Domain: `Trip.AssignVehicle()` + Event

### 2a. เพิ่ม method ใน `Trip`

**File:** `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Entities/Trip.cs`

```csharp
/// <summary>
/// Initial vehicle assignment when RIOT3 auto-selects a robot (VehicleId was null at dispatch).
/// Different from Reassign() which resets in-flight tasks.
/// </summary>
public void AssignVehicle(Guid vehicleId)
{
    if (VehicleId.HasValue)
        throw new InvalidOperationException(
            $"Trip {Id} already has vehicle {VehicleId} assigned. Use Reassign instead.");

    VehicleId = vehicleId;
    AddDomainEvent(new TripVehicleAssignedDomainEvent(Guid.NewGuid(), DateTime.UtcNow, Id, vehicleId));
    RecordEvent(null, "VehicleAssigned", $"Vehicle {vehicleId} assigned by RIOT3");
}
```

### 2b. เพิ่ม domain event

**File:** `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Events/DispatchDomainEvents.cs`

```csharp
public record TripVehicleAssignedDomainEvent(
    Guid EventId, DateTime OccurredOn, Guid TripId, Guid VehicleId) : IDomainEvent;
```

---

## Step 3 — Integration Events (7 events)

**File:** `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.IntegrationEvents/DispatchIntegrationEvents.cs`

```csharp
/// TASK_PROCESSING — VehicleId resolved จาก RIOT3 deviceKey แล้วในชั้น webhook
public record Riot3TaskProcessingIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid TaskId,
    Guid VehicleId,
    string VendorOrderKey) : IIntegrationEvent;

/// TASK_CANCELED
public record Riot3TaskCancelledIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid TaskId,
    string VendorOrderKey,
    string CancelReason) : IIntegrationEvent;

/// TASK_HANG — system-initiated pause (obstacle, traffic control)
public record Riot3TaskHangIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid TaskId,
    string VendorOrderKey,
    string HangReason) : IIntegrationEvent;

/// TASK_HELD — operator-initiated pause
public record Riot3TaskHeldIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid TaskId,
    string VendorOrderKey) : IIntegrationEvent;

/// TASK_HANG_TO_CONTINUE or TASK_HELD_TO_CONTINUE — resume
public record Riot3TaskResumedIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid TaskId,
    string VendorOrderKey) : IIntegrationEvent;

/// SUB_TASK_PROCESSING — subtask started executing
public record Riot3SubTaskProcessingIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid SubTaskId,
    string VendorOrderKey) : IIntegrationEvent;

/// VEHICLE_ENTER_EMERGENCY — vehicle emergency stop triggered
public record Riot3VehicleEmergencyIntegrationEvent(
    Guid EventId, DateTime OccurredOn,
    Guid VehicleId,
    string Detail) : IIntegrationEvent;
```

---

## Step 4 — Webhook Handler Cases

**File:** `src/Modules/VendorAdapter/AMR.DeliveryPlanning.VendorAdapter.Feeder/Webhooks/Riot3Webhooks.cs`

### 4a. `HandleTaskEvent` — เพิ่ม parameter + switch cases

เพิ่ม `IVehicleIdentityResolver vehicleIdentityResolver` parameter (thread through จาก endpoint ที่ inject อยู่แล้ว):

```csharp
// case "task_processing"
var taskUpperKey = payload.Task?.UpperKey ?? payload.UpperKey;
if (!Guid.TryParse(taskUpperKey, out var taskId)) return;

var deviceKey = payload.Task?.ProcessingVehicle?.Key;
if (string.IsNullOrWhiteSpace(deviceKey)) { /* log warning + return */ }

var vehicleId = await vehicleIdentityResolver.ResolveVehicleIdAsync("riot3", deviceKey, cancellationToken);
if (!vehicleId.HasValue) { /* log warning + return */ }

await outbox.AddAsync(new Riot3TaskProcessingIntegrationEvent(
    Guid.NewGuid(), DateTime.UtcNow, taskId, vehicleId.Value, payload.OrderKey ?? ""), cancellationToken);

// case "task_canceled"
await outbox.AddAsync(new Riot3TaskCancelledIntegrationEvent(
    ..., cancelReason: payload.Task?.CancelReason ?? "Cancelled by RIOT3"), cancellationToken);

// case "task_hang"
await outbox.AddAsync(new Riot3TaskHangIntegrationEvent(
    ..., hangReason: payload.Task?.HangReason ?? "System hang"), cancellationToken);

// case "task_held"
await outbox.AddAsync(new Riot3TaskHeldIntegrationEvent(...), cancellationToken);

// case "task_hang_to_continue" / "task_held_to_continue"
await outbox.AddAsync(new Riot3TaskResumedIntegrationEvent(...), cancellationToken);
```

> **Note:** switch ใช้ `.ToLower()` อยู่แล้ว — case strings ต้องเป็น lowercase

### 4b. `HandleSubTaskEvent` — เพิ่ม case

```csharp
// case "sub_task_processing"
if (!Guid.TryParse(payload.TaskId, out var subTaskId)) return;
await outbox.AddAsync(new Riot3SubTaskProcessingIntegrationEvent(
    Guid.NewGuid(), DateTime.UtcNow, subTaskId, payload.OrderKey ?? ""), cancellationToken);
```

### 4c. `HandleVehicleEvent` — แทน log-only

```csharp
// แทน logger.LogWarning เดิม
if (payload.VehicleEventType?.ToUpper() == "VEHICLE_ENTER_EMERGENCY" ||
    vehicle.SafetyState?.Contains("EMERGENCY") == true)
{
    var detail = $"SystemState={vehicle.SystemState}, SafetyState={vehicle.SafetyState}";
    logger.LogWarning("RIOT3 emergency triggered for vehicle {VehicleId}: {Detail}", vehicleId.Value, detail);
    await outbox.AddAsync(new Riot3VehicleEmergencyIntegrationEvent(
        Guid.NewGuid(), DateTime.UtcNow, vehicleId.Value, detail), cancellationToken);
}
```

---

## Step 5 — Commands ใหม่ 2 ตัว

### `AssignTripVehicleCommand`

**Files:**
- `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Commands/AssignTripVehicle/AssignTripVehicleCommand.cs`
- `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Commands/AssignTripVehicle/AssignTripVehicleCommandHandler.cs`

```csharp
public record AssignTripVehicleCommand(Guid TripId, Guid VehicleId) : ICommand;

// Handler: idempotent — skip silently ถ้า VehicleId assigned ไปแล้ว
if (!trip.VehicleId.HasValue)
    trip.AssignVehicle(request.VehicleId);
await _tripRepository.UpdateAsync(trip, ...);
```

### `MarkSubTaskInProgressCommand`

**Files:**
- `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Commands/MarkSubTaskInProgress/MarkSubTaskInProgressCommand.cs`
- `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Commands/MarkSubTaskInProgress/MarkSubTaskInProgressCommandHandler.cs`

```csharp
public record MarkSubTaskInProgressCommand(Guid TripId, Guid SubTaskId) : ICommand;

// Handler: idempotent — skip ถ้า task เป็น InProgress อยู่แล้ว
if (task.Status != TaskStatus.InProgress)
    task.MarkInProgress();
await _tripRepository.UpdateAsync(trip, ...);
```

---

## Step 6 — Consumers (7 ตัว)

**File:** `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Application/Consumers/Riot3TaskEventConsumer.cs`

> MassTransit auto-scan `Dispatch.Application` assembly อยู่แล้ว — ไม่ต้อง register เพิ่ม

| Consumer | Event | Command |
|---|---|---|
| `Riot3TaskProcessingConsumer` | `Riot3TaskProcessingIntegrationEvent` | `AssignTripVehicleCommand` *(new)* |
| `Riot3TaskCancelledConsumer` | `Riot3TaskCancelledIntegrationEvent` | `CancelTripCommand` *(existing)* |
| `Riot3TaskHangConsumer` | `Riot3TaskHangIntegrationEvent` | `PauseTripCommand` *(existing)* |
| `Riot3TaskHeldConsumer` | `Riot3TaskHeldIntegrationEvent` | `PauseTripCommand` *(existing)* |
| `Riot3TaskResumedConsumer` | `Riot3TaskResumedIntegrationEvent` | `ResumeTripCommand` *(existing)* |
| `Riot3SubTaskProcessingConsumer` | `Riot3SubTaskProcessingIntegrationEvent` | `MarkSubTaskInProgressCommand` *(new)* |
| `Riot3VehicleEmergencyConsumer` | `Riot3VehicleEmergencyIntegrationEvent` | `RaiseExceptionCommand` *(existing)* |

### Pattern ปกติ (เหมือน consumers ที่มีอยู่แล้ว)

```csharp
public class Riot3TaskProcessingConsumer : IConsumer<Riot3TaskProcessingIntegrationEvent>
{
    public async Task Consume(ConsumeContext<Riot3TaskProcessingIntegrationEvent> context)
    {
        var trip = await _tripRepo.GetTripByTaskIdAsync(context.Message.TaskId, ...);
        if (trip == null) return; // log + return
        _tenantContext.Set(trip.TenantId);
        await _sender.Send(new AssignTripVehicleCommand(trip.Id, context.Message.VehicleId), ...);
    }
}
```

### `Riot3VehicleEmergencyConsumer` — ต้องใช้ unfiltered query

Vehicle emergency ไม่มี TaskId — ต้องหา active trips จาก VehicleId โดยข้าม tenant query filter:

```csharp
public class Riot3VehicleEmergencyConsumer : IConsumer<Riot3VehicleEmergencyIntegrationEvent>
{
    public async Task Consume(ConsumeContext<Riot3VehicleEmergencyIntegrationEvent> context)
    {
        // ใช้ IgnoreQueryFilters — webhook ไม่มี tenant context
        var trips = await _tripRepo.GetActiveTripsByVehicleIgnoringTenantAsync(
            context.Message.VehicleId, context.CancellationToken);

        foreach (var trip in trips)
        {
            _tenantContext.Set(trip.TenantId);
            await _sender.Send(new RaiseExceptionCommand(
                trip.Id, "EMERGENCY", "Critical", context.Message.Detail), ...);
        }
    }
}
```

---

## Step 7 — Repository Changes

**Files:**
- `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Domain/Repositories/ITripRepository.cs`
- `src/Modules/Dispatch/AMR.DeliveryPlanning.Dispatch.Infrastructure/Repositories/TripRepository.cs`

### 7a. ให้ `UpdateAsync` track `VehicleId`

ในส่วน manual-attach path เพิ่ม:
```csharp
entry.Property(t => t.VehicleId).IsModified = true;
```
ถ้าไม่เพิ่ม — `trip.AssignVehicle()` จะ update ใน memory แต่ไม่บันทึกลง DB

### 7b. เพิ่ม interface + implementation

```csharp
// ITripRepository
Task<IReadOnlyList<Trip>> GetActiveTripsByVehicleIgnoringTenantAsync(
    Guid vehicleId, CancellationToken cancellationToken = default);

// TripRepository implementation
return await _context.Trips
    .IgnoreQueryFilters()
    .Include(t => t.Tasks)
    .Where(t => t.VehicleId == vehicleId
             && t.Status == TripStatus.InProgress)
    .ToListAsync(cancellationToken);
```

---

## สรุปไฟล์ทั้งหมด

| Action | File |
|---|---|
| **Modify** | `VendorAdapter.Riot3/Models/Riot3NotifyPayload.cs` |
| **Modify** | `VendorAdapter.Feeder/Webhooks/Riot3Webhooks.cs` |
| **Modify** | `Dispatch.Domain/Entities/Trip.cs` |
| **Modify** | `Dispatch.Domain/Events/DispatchDomainEvents.cs` |
| **Modify** | `Dispatch.IntegrationEvents/DispatchIntegrationEvents.cs` |
| **Modify** | `Dispatch.Domain/Repositories/ITripRepository.cs` |
| **Modify** | `Dispatch.Infrastructure/Repositories/TripRepository.cs` |
| **Modify** | `Dispatch.Application/Consumers/Riot3TaskEventConsumer.cs` |
| **Create** | `Commands/AssignTripVehicle/AssignTripVehicleCommand.cs` |
| **Create** | `Commands/AssignTripVehicle/AssignTripVehicleCommandHandler.cs` |
| **Create** | `Commands/MarkSubTaskInProgress/MarkSubTaskInProgressCommand.cs` |
| **Create** | `Commands/MarkSubTaskInProgress/MarkSubTaskInProgressCommandHandler.cs` |

> ✅ **ไม่ต้องมี EF Migration เพิ่มเติม** — `Trip.VehicleId` เป็น nullable UUID อยู่แล้วจาก migration `MakeTrip_VehicleId_Nullable`
