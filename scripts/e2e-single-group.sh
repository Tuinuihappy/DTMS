#!/usr/bin/env bash
# E2E test for single-group envelope dispatch flow.
# Verifies: upstream order -> envelope dispatch -> Trip Created ->
#           TASK_PROCESSING webhook -> Trip InProgress + vehicle bound ->
#           TASK_FINISHED webhook -> Trip Completed + DeliveryOrder Completed + items Delivered

set -euo pipefail

API=http://localhost:5219
PG="docker exec dtms-postgres psql -tA -U postgres -d amr_delivery_planning"

red()    { printf "\033[31m%s\033[0m\n" "$*"; }
green()  { printf "\033[32m%s\033[0m\n" "$*"; }
yellow() { printf "\033[33m%s\033[0m\n" "$*"; }
bold()   { printf "\033[1m%s\033[0m\n" "$*"; }

assert_eq() {
  local label="$1" expected="$2" actual="$3"
  if [ "$expected" = "$actual" ]; then
    green "  ✓ $label: $actual"
  else
    red   "  ✗ $label: expected '$expected', got '$actual'"
    FAIL=1
  fi
}

FAIL=0
SCENARIO="${1:-happy}"

bold "=== E2E single-group envelope flow ($SCENARIO) ==="

# ── auth ─────────────────────────────────────────────────────────────
TOKEN=$(curl -sS -X POST $API/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}' \
  | sed -E 's/.*"token":"([^"]+)".*/\1/')
[ -z "$TOKEN" ] && { red "auth failed"; exit 1; }

# ── 1. create upstream order ─────────────────────────────────────────
bold "1. POST upstream order (SHELF4 -> AMF_02)"
ORDER_REF="E2E-$SCENARIO-$(date +%s)"
IDEMP=$(uuidgen 2>/dev/null || echo "$(date +%s%N)-e2e")
BODY=$(cat <<EOF
{
  "orderRef":"$ORDER_REF",
  "serviceWindow":{"earliestUtc":"2026-06-04T10:00:00Z","latestUtc":"2026-06-04T14:00:00Z"},
  "priority":1, "sourceSystem":1, "requestedTransportMode":0,
  "items":[{
    "itemId":"SKU-$SCENARIO-1",
    "description":"$SCENARIO smoke item",
    "pickupLocationCode":"SHELF4",
    "dropLocationCode":"AMF_02",
    "loadUnitProfileCode":null,
    "dimensions":{"lengthMm":100,"widthMm":100,"heightMm":100},
    "weightKg":5.0,
    "quantity":{"value":1,"uom":"EA"}
  }]
}
EOF
)
RESP=$(curl -sS -X POST $API/api/v1/delivery-orders/upstream \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $IDEMP" \
  -d "$BODY")
ORDER_ID=$(echo "$RESP" | sed -E 's/.*"id":"([^"]+)".*/\1/')
STATUS=$(echo "$RESP"  | sed -E 's/.*"status":"([^"]+)".*/\1/')
UPPER_KEY="${ORDER_ID//-/}-G1"
assert_eq "Order accepted as CONFIRMED" "CONFIRMED" "$STATUS"
echo "  OrderId=$ORDER_ID"
echo "  UpperKey=$UPPER_KEY"

sleep 6  # let outbox -> Planning consumer -> Dispatch envelope flow

# ── 2. envelope Trip should exist ────────────────────────────────────
bold "2. Trip created via envelope dispatch"
TRIP_ROW=$($PG -c "SELECT \"Id\"||'|'||\"Status\"||'|'||COALESCE(\"VendorOrderKey\",'') FROM dispatch.\"Trips\" WHERE \"UpperKey\"='$UPPER_KEY';")
TRIP_ID=$(echo "$TRIP_ROW"     | cut -d'|' -f1)
TRIP_STATUS=$(echo "$TRIP_ROW" | cut -d'|' -f2)
VENDOR_KEY=$(echo "$TRIP_ROW"  | cut -d'|' -f3)
assert_eq "Trip status" "Created" "$TRIP_STATUS"
[ -n "$TRIP_ID" ] && green "  ✓ TripId=$TRIP_ID" || { red "  ✗ no Trip row found"; FAIL=1; }
echo "  vendorOrderKey=\"${VENDOR_KEY}\" (RIOT3 may echo empty)"

# ── 3. simulate TASK_PROCESSING ──────────────────────────────────────
bold "3. webhook TASK_PROCESSING (with processingVehicle)"
VEHICLE_ID=$(uuidgen 2>/dev/null || echo "ffffffff-1111-2222-3333-444444444444")
WH=$(cat <<EOF
{
  "type":"taskNotify",
  "taskEventType":"TASK_PROCESSING",
  "task":{
    "name":"$SCENARIO smoke",
    "key":"",
    "upperKey":"$UPPER_KEY",
    "state":"PROCESSING",
    "processingVehicle":{"key":"$VEHICLE_ID","name":"SEER-001"}
  }
}
EOF
)
CODE=$(curl -sS -o /dev/null -w "%{http_code}" -X POST $API/api/webhooks/riot3/notify \
  -H "Content-Type: application/json" -d "$WH")
assert_eq "Webhook HTTP" "200" "$CODE"
sleep 2
TRIP_STATUS=$($PG -c "SELECT \"Status\" FROM dispatch.\"Trips\" WHERE \"Id\"='$TRIP_ID';")
TRIP_VEH=$($PG    -c "SELECT COALESCE(\"VehicleId\"::text,'(null)') FROM dispatch.\"Trips\" WHERE \"Id\"='$TRIP_ID';")
assert_eq "Trip status" "InProgress" "$TRIP_STATUS"
assert_eq "Vehicle bound" "$VEHICLE_ID" "$TRIP_VEH"

# ── 4. simulate terminal event ───────────────────────────────────────
if [ "$SCENARIO" = "happy" ]; then
  bold "4. webhook TASK_FINISHED"
  WH=$(cat <<EOF
{"type":"taskNotify","taskEventType":"TASK_FINISHED",
 "task":{"name":"$SCENARIO smoke","key":"","upperKey":"$UPPER_KEY","state":"FINISHED"}}
EOF
)
elif [ "$SCENARIO" = "fail" ]; then
  bold "4. webhook TASK_FAILED"
  WH=$(cat <<EOF
{"type":"taskNotify","taskEventType":"TASK_FAILED",
 "task":{"name":"$SCENARIO smoke","key":"","upperKey":"$UPPER_KEY","state":"FAILED",
         "failReason":{"errorCode":"OBSTACLE","errorDescription":"path blocked"}}}
EOF
)
fi

CODE=$(curl -sS -o /dev/null -w "%{http_code}" -X POST $API/api/webhooks/riot3/notify \
  -H "Content-Type: application/json" -d "$WH")
assert_eq "Webhook HTTP" "200" "$CODE"
sleep 8  # let TripCompleted/Failed event flow through outbox -> DeliveryOrder consumer

# ── 5. verify Trip terminal state ────────────────────────────────────
bold "5. Trip terminal state"
TRIP_ROW=$($PG -c "SELECT \"Status\"||'|'||COALESCE(\"FailureReason\",'') FROM dispatch.\"Trips\" WHERE \"Id\"='$TRIP_ID';")
TRIP_STATUS=$(echo "$TRIP_ROW" | cut -d'|' -f1)
TRIP_REASON=$(echo "$TRIP_ROW" | cut -d'|' -f2)
if [ "$SCENARIO" = "happy" ]; then
  assert_eq "Trip status" "Completed" "$TRIP_STATUS"
else
  assert_eq "Trip status" "Failed" "$TRIP_STATUS"
  assert_eq "Failure reason" "path blocked" "$TRIP_REASON"
fi

# ── 6. verify DeliveryOrder propagation ──────────────────────────────
bold "6. DeliveryOrder propagation"
ORDER_STATUS=$($PG -c "SELECT \"Status\" FROM deliveryorder.\"DeliveryOrders\" WHERE \"Id\"='$ORDER_ID';")
ITEM_STATUS=$($PG  -c "SELECT \"Status\" FROM deliveryorder.\"Items\" WHERE \"DeliveryOrderId\"='$ORDER_ID' LIMIT 1;")
if [ "$SCENARIO" = "happy" ]; then
  assert_eq "DeliveryOrder status" "Completed" "$ORDER_STATUS"
  assert_eq "Item status"          "Delivered" "$ITEM_STATUS"
else
  assert_eq "DeliveryOrder status" "Failed" "$ORDER_STATUS"
fi

# ── result ───────────────────────────────────────────────────────────
echo
if [ "$FAIL" = "0" ]; then
  green "════ E2E $SCENARIO PASSED ════"
  exit 0
else
  red "════ E2E $SCENARIO FAILED ════"
  exit 1
fi
