#!/bin/bash
set -e
API=http://localhost:5219

echo ">> create map"
MAP_ID=$(curl -s -X POST "$API/api/v1/facility/maps" \
  -H "Content-Type: application/json" \
  -d '{"name":"loadtest-map","version":"1","width":1000,"height":1000,"mapData":"{}"}' | tr -d '"')
echo "map=$MAP_ID"

echo ">> add station WH-01 (pickup)"
curl -s -X POST "$API/api/v1/facility/maps/$MAP_ID/stations" \
  -H "Content-Type: application/json" \
  -d '{"name":"Warehouse 01","x":10,"y":10,"theta":0,"type":"NORMAL","code":"WH-01"}' | head -c 200
echo

echo ">> add station BAY-A (drop)"
curl -s -X POST "$API/api/v1/facility/maps/$MAP_ID/stations" \
  -H "Content-Type: application/json" \
  -d '{"name":"Bay A","x":500,"y":500,"theta":0,"type":"NORMAL","code":"BAY-A"}' | head -c 200
echo

echo ">> add station BAY-B (drop)"
curl -s -X POST "$API/api/v1/facility/maps/$MAP_ID/stations" \
  -H "Content-Type: application/json" \
  -d '{"name":"Bay B","x":600,"y":600,"theta":0,"type":"NORMAL","code":"BAY-B"}' | head -c 200
echo

echo ">> register carrier-type-profile PALLET-CARRIER"
curl -s -X POST "$API/api/v1/facility/carrier-type-profiles" \
  -H "Content-Type: application/json" \
  -d '{"code":"PALLET-CARRIER","displayName":"Pallet Carrier","amrCapability":"PALLET","maxWeightKg":1500,"maxSlots":1}' | head -c 200
echo

echo ">> register load-unit-profile PALLET-EU"
curl -s -X POST "$API/api/v1/facility/load-unit-profiles" \
  -H "Content-Type: application/json" \
  -d '{"code":"PALLET-EU","displayName":"Euro Pallet","lengthMm":1200,"widthMm":800,"heightMm":1500,"maxGrossWeightKg":1000,"carrierTypeCode":"PALLET-CARRIER"}' | head -c 200
echo
echo ">> done"
