# RIOT3 Robot Inventory

> Retrieved: 2026-04-30  
> Last verified: 2026-04-30  
> Source: `GET http://10.204.212.28:12000/api/v4/robots`  
> Auth token: not stored in this file

## Summary

RIOT3 returned `code = 0`, `message = SUCCESS`, total robots = 1.

| # | deviceKey | deviceName | group | connection | dispatch | operating mode | systemState | battery | emergency | station | map | current IP |
|---:|---|---|---|---|---|---|---|---:|---|---|---|---|
| 1 | `0fc4021c-604d-41d9-bcdb-ae6d13f82133` | `FAN1_FEEDER_NO6` | `FAN1_FEEDER` | `ONLINE` | `ENABLE` | `AUTOMATIC` | `IDLE` | 62% | false | `FAN1_MAP_06242025A-FAN1_DOCK6` | `FAN1_MAP_06242025A` | `10.192.135.27` |

## DTMS Registration Evidence

Verified against local DTMS API at `http://localhost:5219` on 2026-04-30:

```json
{
  "id": "df62ecc9-9c6a-408d-b98d-ea74ac5b7629",
  "vehicleName": "FAN1_FEEDER_NO6",
  "vehicleTypeId": "b1af631f-3817-4a74-8420-7c71e85601a8",
  "adapterKey": "riot3",
  "vendorVehicleKey": "0fc4021c-604d-41d9-bcdb-ae6d13f82133"
}
```

## App Registration Draft

Create or select a vehicle type first:

```http
POST /api/fleet/vehicle-types
{
  "typeName": "RIOT3 Feeder",
  "maxPayload": 100.0,
  "capabilities": ["MOVE", "LIFT"]
}
```

Use this mapping when registering the robot in DTMS Fleet:

```http
POST /api/fleet/vehicles
{
  "vehicleName": "FAN1_FEEDER_NO6",
  "vehicleTypeId": "<vehicle-type-id>",
  "adapterKey": "riot3",
  "vendorVehicleKey": "0fc4021c-604d-41d9-bcdb-ae6d13f82133"
}
```

## Operational Notes

- The robot is currently connected (`ONLINE`) and RIOT3 dispatch is enabled.
- Battery is `62%`.
- `emergency = false`.
- `operatingMode = AUTOMATIC`, `systemState = IDLE`, `scheduled = true`, `paused = false`, `errorEntryList = 0`.
- Latest status list reports `online`, `idle`, and `load`; robot is ready for controlled R3-5 dry run from the RIOT3 health gate perspective.
- RIOT3 reports map `FAN1_MAP_06242025A`; DTMS station vendor refs must align with this RIOT3 map/station data before E2E dry run.
- Local DTMS webhook smoke test passed on 2026-04-30: `POST http://localhost:5219/api/webhooks/riot3/notify` returned `200 OK`.

## R3-2 Status

- [x] Robot inventory fetched from RIOT3.
- [x] `deviceKey` captured for DTMS `VendorVehicleKey` mapping.
- [x] DTMS API supports creating VehicleType via `POST /api/fleet/vehicle-types`.
- [x] DTMS vehicle record registered in the current target environment.
- [x] VehicleType selected/created for `FAN1_FEEDER_NO6` in the current target environment: `b1af631f-3817-4a74-8420-7c71e85601a8`.
- [x] Robot dispatch/schedule ready for R3-5 dry run. Latest check is `ONLINE`, `dispatchState = ENABLE`, `scheduled = true`, `AUTOMATIC`, `IDLE`, no emergency/errors.

## R3-3 Webhook Callback Evidence

Verified against RIOT3 app configuration on 2026-04-30:

| App | Status | Notifications | IP | Port | URL | HTTPS |
|---|---|---|---|---:|---|---|
| `Delta6FAN1` | enabled | enabled | `10.204.37.31` | 5219 | `/api/webhooks/riot3/notify` | disabled |

RIOT3 app API returned `code = 0`, `message = SUCCESS`, with notify events enabled for task lifecycle events including `TASK_FINISHED`, `TASK_FAILED`, and `TASK_CANCELED`.

Task notification events enabled for `Delta6FAN1`:

```text
TASK_CREATE, TASK_UPDATE, TASK_FINISHED, TASK_FAILED, TASK_CANCELED,
TASK_HANG, TASK_HANG_TO_CONTINUE, TASK_HELD, TASK_HELD_TO_CONTINUE,
TASK_PROCESSING, TASK_REJECTED, TASK_QUEUEING, TASK_CHANGE_VEHICLE
```

Callback URL for this environment:

```text
http://10.204.37.31:5219/api/webhooks/riot3/notify
```

Local network smoke test passed on 2026-04-30:

```text
POST http://10.204.37.31:5219/api/webhooks/riot3/notify -> 200 OK
```

RIOT3 -> DTMS inbound callback evidence from `dtms-api` logs on 2026-04-30:

```text
[09:02:22 WRN] Unknown RIOT3 notify type: taskNotify
[09:02:22 INF] HTTP POST /api/webhooks/riot3/notify responded 200
[09:05:42 WRN] Unknown RIOT3 notify type: subTaskNotify
[09:05:42 INF] HTTP POST /api/webhooks/riot3/notify responded 200
[09:07:07 WRN] Unknown RIOT3 notify type: vehicleNotify
[09:07:07 INF] HTTP POST /api/webhooks/riot3/notify responded 200
```

R3-3 status:

- [x] RIOT3 app callback points to `10.204.37.31:5219`.
- [x] RIOT3 notification URL is `/api/webhooks/riot3/notify`.
- [x] Notifications are enabled.
- [x] HTTPS is disabled for this local network callback.
- [x] RIOT3 real callbacks reached DTMS and received `200 OK`.

## R3-4 Network Path Evidence

Verified on 2026-04-30.

### App -> RIOT3

DTMS can call RIOT3 over the production network path:

```text
GET http://10.204.212.28:12000/api/v4/robots -> code=0, message=SUCCESS
robotCount=1
deviceName=FAN1_FEEDER_NO6
deviceKey=0fc4021c-604d-41d9-bcdb-ae6d13f82133
connection=ONLINE
dispatch=ENABLE
systemState=IDLE
```

### RIOT3 -> DTMS

DTMS webhook URL is reachable and returns `200 OK`:

```text
POST http://10.204.37.31:5219/api/webhooks/riot3/notify -> 200 OK
```

RIOT3 real callbacks also reached `dtms-api` and were acknowledged:

```text
[09:02:22 WRN] Unknown RIOT3 notify type: taskNotify
[09:02:22 INF] HTTP POST /api/webhooks/riot3/notify responded 200
[09:05:42 WRN] Unknown RIOT3 notify type: subTaskNotify
[09:05:42 INF] HTTP POST /api/webhooks/riot3/notify responded 200
[09:07:07 WRN] Unknown RIOT3 notify type: vehicleNotify
[09:07:07 INF] HTTP POST /api/webhooks/riot3/notify responded 200
```

Semantic alias handling was deployed and verified on 2026-04-30:

```text
POST type=vehicleNotify -> 200 OK
Outbox: VehicleStateChangedIntegrationEvent
OccurredOnUtc=2026-04-30 09:26:44 UTC
ProcessedOnUtc=2026-04-30 09:26:46 UTC
VehicleId=df62ecc9-9c6a-408d-b98d-ea74ac5b7629
State=Idle
BatteryLevel=0.62
Error=<empty>
```

R3-4 status:

- [x] App -> RIOT3 connectivity verified.
- [x] DTMS webhook endpoint reachable at configured callback URL.
- [x] RIOT3 -> DTMS callback path verified from real RIOT3 notifications.
- [x] No firewall/DNS/ingress blocker observed for current local network callback.
- [x] Semantic processing of real RIOT3 callback aliases deployed/tested (`taskNotify`, `subTaskNotify`, `vehicleNotify`).
