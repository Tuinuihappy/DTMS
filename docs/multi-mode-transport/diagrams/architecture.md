# Architecture Diagrams

Mermaid diagrams สำหรับ multi-mode transport architecture หลัง Phase 5

## 1. Module Dependency Graph

```mermaid
graph TB
    subgraph "Shared Kernel"
        DO[DeliveryOrder]
        PL[Planning]
        FAC[Facility]
        DSP[Dispatch]
        OMS[OmsAdapter]
        VEH[Vehicle]
    end

    subgraph "Transport Modes"
        TA[Transport.Abstractions]
        AMR[Transport.Amr]
        MAN[Transport.Manual]
        FLT[Transport.Fleet]
    end

    subgraph "Composition Root"
        API[DTMS.Api]
    end

    DO --> FAC
    PL --> DO
    PL --> FAC
    DSP --> DO
    DSP --> FAC
    DSP --> TA

    AMR --> TA
    AMR --> DSP
    AMR --> FAC
    AMR --> VEH
    AMR --> PL

    MAN --> TA
    MAN --> DSP
    MAN --> FAC

    FLT --> TA
    FLT --> DSP
    FLT --> FAC

    API --> AMR
    API --> MAN
    API --> FLT
    API --> OMS

    style TA fill:#ffd
    style AMR fill:#dfd
    style MAN fill:#ddf
    style FLT fill:#fdd
    style DSP fill:#eee
```

**Key rules:**
- `Dispatch` reference only `Transport.Abstractions` — ไม่รู้จัก concrete mode implementations
- `Transport.{Amr,Manual,Fleet}` ไม่ reference กัน
- `Transport.*` reference `Transport.Abstractions` + shared kernel
- `Api` (composition root) reference all transport modes ผ่าน `AddTransport*()` extensions

## 2. AMR Dispatch Flow (Existing — preserved)

```mermaid
sequenceDiagram
    autonumber
    participant Client as Client/Planning
    participant Cmd as CreateEnvelopeTripHandler
    participant Reg as IDispatchStrategyRegistry
    participant Amr as AmrDispatchStrategy
    participant Riot3 as Riot3CommandService
    participant Vendor as RIOT3 API
    participant Webhook as Riot3Webhooks
    participant Trip as Trip Aggregate

    Client->>Cmd: CreateEnvelopeTripCommand(orderId)
    Cmd->>Trip: new Trip(TransportMode.Amr)
    Cmd->>Reg: Get(TransportMode.Amr)
    Reg-->>Cmd: AmrDispatchStrategy
    Cmd->>Amr: DispatchAsync(trip)

    Amr->>Amr: Build AmrDispatchPlan (missions)
    Amr->>Riot3: SendOrderAsync(plan)
    Riot3->>Vendor: POST /api/v4/orders
    Vendor-->>Riot3: { orderKey, code: "0" }
    Riot3-->>Amr: DispatchResult(success, orderKey)

    Amr->>Amr: Create AmrTripExtension(orderKey)
    Amr-->>Cmd: DispatchResult(success)
    Cmd->>Trip: state = Created (vendor accepted)

    Note over Vendor,Webhook: RIOT3 begins executing
    Vendor->>Webhook: POST /api/webhooks/riot3/notify (TASK_PROCESSING)
    Webhook->>Trip: MarkInProgress()
    Webhook->>Trip: AmrTripExtension.RecordVendorVehicle(deviceKey)

    Note over Vendor,Webhook: Robot completes task
    Vendor->>Webhook: POST notify (TASK_FINISHED)
    Webhook->>Trip: MarkCompleted()
```

## 3. Manual Dispatch Flow (NEW Phase 4)

```mermaid
sequenceDiagram
    autonumber
    participant Client as Client/Planning
    participant Cmd as CreateEnvelopeTripHandler
    participant Man as ManualDispatchStrategy
    participant Policy as IOperatorAssignmentPolicy
    participant Shift as OperatorShift
    participant Push as IPushNotificationGateway
    participant Mobile as Operator Mobile App
    participant Trip as Trip Aggregate
    participant Watchdog as SlaWatchdog

    Client->>Cmd: CreateEnvelopeTripCommand(orderId)
    Cmd->>Trip: new Trip(TransportMode.Manual)
    Cmd->>Man: DispatchAsync(trip)

    Man->>Policy: SelectAsync(trip)
    Policy-->>Man: operatorId
    Man->>Shift: AssignTrip(tripId) [enforces 1-active invariant]
    Man->>Man: Create ManualTripExtension(operatorId, SLA deadlines)
    Man->>Push: SendAsync(operatorId, TripAssignedNotification)
    Push->>Mobile: FCM push notification
    Man-->>Cmd: DispatchResult(success)

    Note over Mobile,Trip: Operator interacts with mobile app

    Mobile->>Cmd: POST /api/operator/trips/{id}/acknowledge
    Cmd->>Trip: MarkInProgress()
    Cmd->>Cmd: ManualTripExtension.RecordAcknowledged()

    Note over Mobile: Operator travels to pickup
    Mobile->>Cmd: POST /api/operator/trips/{id}/pickup<br/>{ gps, photoUrl }
    Cmd->>Cmd: Verify GPS in warehouse geofence
    alt GPS outside geofence
        Cmd-->>Mobile: 400 BadRequest (geofence violation)
    else GPS in geofence
        Cmd->>Cmd: ManualTripExtension.RecordPickup(gps, photo)
        Cmd-->>Mobile: 200 OK
    end

    Mobile->>Cmd: POST /api/operator/trips/{id}/drop<br/>{ gps, signature, photo }
    Cmd->>Cmd: Verify geofence + record POD
    Mobile->>Cmd: POST /api/operator/trips/{id}/complete
    Cmd->>Trip: MarkCompleted()

    Note over Watchdog: Background — runs every 1 min
    Watchdog->>Watchdog: Find ManualTripExtensions where SLA expired
    Watchdog->>Push: Send escalation to dispatcher
```

## 4. Fleet Dispatch Flow (NEW Phase 5)

```mermaid
sequenceDiagram
    autonumber
    participant Client as Client/Planning
    participant Cmd as CreateEnvelopeTripHandler
    participant Flt as FleetDispatchStrategy
    participant Sel as IFleetProviderSelector
    participant Kerry as KerryFleetProviderClient
    participant API as Kerry API
    participant Webhook as FleetWebhookEndpoint
    participant Trip as Trip Aggregate
    participant Recon as WaybillReconciliation

    Client->>Cmd: CreateEnvelopeTripCommand(orderId)
    Cmd->>Trip: new Trip(TransportMode.Fleet)
    Cmd->>Flt: DispatchAsync(trip)

    Flt->>Sel: SelectAsync(destination)
    Sel->>Sel: Filter providers by ServiceArea<br/>+ active contract + best rate
    Sel-->>Flt: Kerry provider
    Flt->>Kerry: CreateShipmentAsync(plan)
    Kerry->>API: POST /api/v1/shipments
    API-->>Kerry: { waybillNumber, shipmentId, eta }
    Kerry-->>Flt: CreateShipmentResult(success)
    Flt->>Flt: Persist Waybill + FleetTripExtension
    Flt-->>Cmd: DispatchResult(success, waybillNumber)

    Note over API,Webhook: Kerry picks up shipment
    API->>Webhook: POST /api/webhooks/fleet/kerry/notify<br/>{ event: "PICKED_UP" }
    Webhook->>Webhook: Verify HMAC signature
    Webhook->>Webhook: Map status → WaybillStatus.PickedUp
    Webhook->>Trip: (status update only, no Trip transition yet)

    API->>Webhook: POST notify { event: "DELIVERED" }
    Webhook->>Trip: MarkCompleted()

    Note over Recon: Background — every 10 min
    Recon->>Recon: Find in-flight waybills
    loop For each
        Recon->>Kerry: GetShipmentStatusAsync(providerRef)
        Kerry->>API: GET /api/v1/shipments/{id}
        API-->>Kerry: status
        alt Status changed from last sync
            Recon->>Webhook: Update waybill (same path as webhook)
        end
    end
```

## 5. Trip FSM (Shared across all modes)

```mermaid
stateDiagram-v2
    [*] --> Created: CreateEnvelopeTrip<br/>(strategy dispatched)

    Created --> InProgress: Mode-specific signal<br/>(RIOT3 TASK_PROCESSING<br/>/ Operator acknowledge<br/>/ Provider accepts)
    Created --> Cancelled: CancelTripCommand
    Created --> Failed: dispatch error

    InProgress --> Paused: PauseTripCommand
    InProgress --> Completed: terminal success<br/>(RIOT3 TASK_FINISHED<br/>/ Operator complete<br/>/ Provider DELIVERED)
    InProgress --> Failed: terminal failure<br/>(RIOT3 TASK_FAILED<br/>/ SLA breach<br/>/ Provider FAILED)
    InProgress --> Cancelled: CancelTripCommand

    Paused --> InProgress: ResumeTripCommand
    Paused --> Failed: vendor drift detected
    Paused --> Cancelled: CancelTripCommand

    Completed --> [*]
    Failed --> [*]
    Cancelled --> [*]

    note right of InProgress
        For Manual: also tracks
        - Acknowledged
        - PickedUp
        - Dropped
        as sub-states via
        ManualTripExtension timestamps
    end note
```

## 6. Pause/Resume Router Flow (Mode-Agnostic)

```mermaid
sequenceDiagram
    autonumber
    participant UI as Dispatcher Console
    participant Cmd as PauseTripCommandHandler
    participant Router as IVendorOperationsRouter
    participant Amr as Riot3VendorEnvelopeOpAdapter
    participant Man as ManualVendorEnvelopeOpAdapter
    participant Flt as FleetVendorEnvelopeOpAdapter
    participant Trip as Trip Aggregate

    UI->>Cmd: POST /trips/{id}/pause
    Cmd->>Trip: Load(tripId)
    Cmd->>Router: For(trip)

    alt trip.TransportMode == Amr
        Router-->>Cmd: Riot3 adapter
        Cmd->>Amr: PauseAsync(tripId)
        Amr->>Amr: Read AmrTripExtension.VendorOrderKey
        Amr->>Amr: RIOT3 PauseEnvelopeAsync(orderKey)
    else trip.TransportMode == Manual
        Router-->>Cmd: Manual adapter
        Cmd->>Man: PauseAsync(tripId)
        Man->>Man: Read ManualTripExtension.OperatorId
        Man->>Man: Send push notification to operator
    else trip.TransportMode == Fleet
        Router-->>Cmd: Fleet adapter
        Cmd->>Flt: PauseAsync(tripId)
        Flt->>Flt: Most 3PLs don't support pause
        Flt-->>Cmd: Rejected
    end

    alt Outcome == Accepted
        Cmd->>Trip: MarkPaused()
    else Outcome == Rejected
        Cmd-->>UI: 422 Unprocessable (vendor rejected)
    else Outcome == NoVendorRecord
        Cmd->>Trip: MarkFailed("vendor lost trip")
    end
```

## 7. Facility / AmrStation Hierarchy

```mermaid
erDiagram
    FACILITY ||--o{ AMR_MAP : "has 0..N"
    FACILITY ||--o{ ITEM_PICKUP : "items pickup at"
    FACILITY ||--o{ ITEM_DROP : "items drop at"
    AMR_MAP ||--o{ AMR_STATION : "contains"
    ITEM_PICKUP }o--o| AMR_STATION : "AMR mode requires"
    ITEM_DROP }o--o| AMR_STATION : "AMR mode requires"

    FACILITY {
        guid id PK
        string code UK
        string name
        latlng location
        address address
        int geofence_radius_m
        jsonb service_modes
        bool is_active
    }

    AMR_MAP {
        guid id PK
        guid facility_id FK
        string vendor_ref "RIOT3 map ID"
        string name
    }

    AMR_STATION {
        guid id PK
        guid map_id FK
        guid facility_id FK "denormalized"
        string code
        string vendor_ref "RIOT3 station ID"
        coordinate coordinate "factory-local x,y,theta"
        string type
        jsonb actions
    }

    ITEM_PICKUP {
        guid pickup_warehouse_id FK "required"
        guid pickup_station_id FK "AMR-only"
    }
```

## 8. Trip Extension Tables

```mermaid
erDiagram
    TRIP ||--o| AMR_TRIP_EXT : "if mode=Amr"
    TRIP ||--o| MANUAL_TRIP_EXT : "if mode=Manual"
    TRIP ||--o| FLEET_TRIP_EXT : "if mode=Fleet"

    TRIP {
        guid id PK
        guid order_id FK
        string transport_mode "discriminator"
        string status
        guid pickup_warehouse_id FK
        guid drop_warehouse_id FK
        guid pickup_station_id FK "nullable"
        guid drop_station_id FK "nullable"
        guid vehicle_id FK "nullable"
    }

    AMR_TRIP_EXT {
        guid trip_id PK_FK
        string vendor_order_key
        string vendor_vehicle_key
        string vendor_vehicle_name
        string vendor_pause_source
        jsonb vendor_request_snapshot
    }

    MANUAL_TRIP_EXT {
        guid trip_id PK_FK
        guid assigned_operator_id FK
        guid operator_shift_id FK
        timestamp acknowledged_at
        timestamp pickup_verified_at
        timestamp drop_verified_at
        latlng pickup_gps
        latlng drop_gps
        string pod_photo_url
        string pod_signature_url
        timestamp expected_pickup_by
        timestamp expected_drop_by
    }

    FLEET_TRIP_EXT {
        guid trip_id PK_FK
        guid waybill_id FK
        guid provider_id FK
        string waybill_number
        string tracking_url
        timestamp estimated_arrival_at
    }
```

## 9. End-to-End: 3 Orders Parallel

```mermaid
gantt
    title 3 Orders Dispatched Simultaneously
    dateFormat HH:mm
    axisFormat %H:%M

    section Order 1 (AMR)
    Create order            :done, a1, 10:00, 1m
    Dispatch (Riot3)        :done, a2, after a1, 1m
    RIOT3 executes          :done, a3, after a2, 8m
    Webhook complete        :done, a4, after a3, 1m

    section Order 2 (Manual)
    Create order            :done, m1, 10:01, 1m
    Assign operator + push  :done, m2, after m1, 1m
    Operator ack            :done, m3, after m2, 3m
    Drive to pickup         :done, m4, after m3, 5m
    Pickup (geofence + POD) :done, m5, after m4, 2m
    Drive to drop           :done, m6, after m5, 10m
    Drop (POD)              :done, m7, after m6, 2m
    Complete                :done, m8, after m7, 1m

    section Order 3 (Fleet)
    Create order            :done, f1, 10:02, 1m
    Select Kerry            :done, f2, after f1, 1m
    POST shipment           :done, f3, after f2, 1m
    Webhook PICKED_UP       :done, f4, 12:00, 1m
    Webhook IN_TRANSIT      :done, f5, after f4, 4h
    Webhook DELIVERED       :done, f6, 16:00, 1m
```

> All 3 trips share Trip table + projections + dispatcher console — different code paths for vendor ops, same FSM
