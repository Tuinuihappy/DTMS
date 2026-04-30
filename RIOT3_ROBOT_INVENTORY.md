# RIOT3 Robot Inventory

> Retrieved: 2026-04-30  
> Last verified: 2026-04-30  
> Source: `GET http://10.204.212.28:12000/api/v4/robots`  
> Auth token: not stored in this file

## Summary

RIOT3 returned `code = 0`, `message = SUCCESS`, total robots = 1.

| # | deviceKey | deviceName | group | connection | dispatch | operating mode | systemState | battery | emergency | station | map | current IP |
|---:|---|---|---|---|---|---|---|---:|---|---|---|---|
| 1 | `0fc4021c-604d-41d9-bcdb-ae6d13f82133` | `FAN1_FEEDER_NO6` | `FAN1_FEEDER` | `ONLINE` | `ENABLE` | `AUTOMATIC` | `IDLE` | 68% | false | `FAN1_MAP_06242025A-FAN1_DOCK6` | `FAN1_MAP_06242025A` | `10.192.135.27` |

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
- Battery is `68%`.
- `emergency = false`.
- `operatingMode = AUTOMATIC`, `systemState = IDLE`, `scheduled = true`, `paused = false`, `errorEntryList = 0`.
- Latest status list only reports `online` and `idle`; robot is ready for controlled R3-5 dry run from the RIOT3 health gate perspective.
- RIOT3 reports map `FAN1_MAP_06242025A`; DTMS station vendor refs must align with this RIOT3 map/station data before E2E dry run.

## R3-2 Status

- [x] Robot inventory fetched from RIOT3.
- [x] `deviceKey` captured for DTMS `VendorVehicleKey` mapping.
- [x] DTMS API supports creating VehicleType via `POST /api/fleet/vehicle-types`.
- [x] DTMS vehicle record registered in the current target environment.
- [x] VehicleType selected/created for `FAN1_FEEDER_NO6` in the current target environment: `b1af631f-3817-4a74-8420-7c71e85601a8`.
- [x] Robot brought fully healthy for R3-5 dry run (`ONLINE`, dispatch enabled, `AUTOMATIC`, `IDLE`, no emergency/errors, scheduled).
