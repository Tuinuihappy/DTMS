# Delivery Order — Payload Examples

## Design Overview

Package-first (nested) model:

- `orderItems[]` = transport units (PackageUnit) ที่ AMR จะเคลื่อนย้าย
- `contents[]` = items ข้างใน (optional — ไม่มี = self-contained unit)
- `carrierTypeCode` = derive อัตโนมัติจาก `loadUnitProfileCode`
- Leg แยกอัตโนมัติตาม `(pickup, drop, carrierTypeCode)`

---

## Field Reference

### OrderItem (PackageUnit)

| Field | Required | Description |
|-------|----------|-------------|
| `barcode` | ✅ | Barcode ของกล่อง / item |
| `pickupLocationCode` | ✅ | สถานที่รับ |
| `dropLocationCode` | ✅ | สถานที่ส่ง |
| `loadUnitProfileCode` | ✅ | ประเภทกล่อง → กำหนด carrier type |
| `grossWeightKg` | ✅ | น้ำหนักรวม (กล่อง + ของข้างใน) |
| `dims` | ❌ | ขนาดจริง (ถ้าต่างจาก profile) |
| `contents[]` | ❌ | ของข้างใน (ไม่มี = loose item) |

### Content Item

| Field | Required | Description |
|-------|----------|-------------|
| `itemNumber` | ✅ | รหัส item |
| `quantity` | ✅ | จำนวน |
| `itemStatus` | ❌ | สภาพวัสดุ: `Good`, `Defective`, `Quarantine`, `Hold`, `Damaged`, `Rejected` |

---

## Case 1: Material (วัสดุ / WIP)

**POST** `/api/delivery-orders`

```json
{
  "orderName": "ORD-MATERIAL-001",
  "slaTier": "Normal",
  "structureType": "Sequence",
  "serviceWindow": { "earliest": null, "latest": null },
  "tags": ["material"],
  "orderItems": [
    {
      "barcode": "BOX-MAT-001",
      "pickupLocationCode": "STORE-A",
      "dropLocationCode": "WORKSTATION-01",
      "loadUnitProfileCode": "CARTON-A3",
      "grossWeightKg": 12.0,
      "contents": [
        { "itemNumber": "MOTOR-A", "quantity": 5, "itemStatus": "Good" },
        { "itemNumber": "PCB-B",   "quantity": 3, "itemStatus": "Defective" }
      ]
    },
    {
      "barcode": "BOX-MAT-002",
      "pickupLocationCode": "STORE-A",
      "dropLocationCode": "WORKSTATION-01",
      "loadUnitProfileCode": "CARTON-A3",
      "grossWeightKg": 8.5,
      "contents": [
        { "itemNumber": "GASKET-X", "quantity": 10, "itemStatus": "Good" }
      ]
    }
  ]
}
```

**GET** `/api/delivery-orders/{id}`

```json
{
  "id": "...",
  "orderName": "ORD-MATERIAL-001",
  "slaTier": "Normal",
  "status": "Draft",
  "legs": [
    {
      "sequence": 1,
      "pickupLocationCode": "STORE-A",
      "dropLocationCode": "WORKSTATION-01",
      "carrierTypeCode": "SHELF",
      "pickupStationId": null,
      "dropStationId": null,
      "packages": [
        {
          "barcode": "BOX-MAT-001",
          "loadUnitProfileCode": "CARTON-A3",
          "grossWeightKg": 12.0,
          "status": "Pending",
          "contents": [
            { "itemNumber": "MOTOR-A", "quantity": 5, "itemStatus": "Good" },
            { "itemNumber": "PCB-B",   "quantity": 3, "itemStatus": "Defective" }
          ]
        },
        {
          "barcode": "BOX-MAT-002",
          "loadUnitProfileCode": "CARTON-A3",
          "grossWeightKg": 8.5,
          "status": "Pending",
          "contents": [
            { "itemNumber": "GASKET-X", "quantity": 10, "itemStatus": "Good" }
          ]
        }
      ]
    }
  ]
}
```

---

## Case 2: Fixture (เครื่องมือ / Jig)

ไม่มี `contents` — item คือ transport unit ของตัวเอง

**POST**

```json
{
  "orderName": "ORD-FIXTURE-001",
  "slaTier": "Normal",
  "orderItems": [
    {
      "barcode": "JIG-SN-0042",
      "pickupLocationCode": "TOOL-ROOM",
      "dropLocationCode": "LINE-3",
      "loadUnitProfileCode": "FIXTURE-FRAME",
      "grossWeightKg": 15.0
    },
    {
      "barcode": "JIG-SN-0055",
      "pickupLocationCode": "TOOL-ROOM",
      "dropLocationCode": "LINE-3",
      "loadUnitProfileCode": "FIXTURE-FRAME",
      "grossWeightKg": 12.0
    }
  ]
}
```

**GET**

```json
{
  "legs": [
    {
      "sequence": 1,
      "pickupLocationCode": "TOOL-ROOM",
      "dropLocationCode": "LINE-3",
      "carrierTypeCode": "DIRECT",
      "packages": [
        {
          "barcode": "JIG-SN-0042",
          "loadUnitProfileCode": "FIXTURE-FRAME",
          "grossWeightKg": 15.0,
          "status": "Pending",
          "contents": []
        },
        {
          "barcode": "JIG-SN-0055",
          "loadUnitProfileCode": "FIXTURE-FRAME",
          "grossWeightKg": 12.0,
          "status": "Pending",
          "contents": []
        }
      ]
    }
  ]
}
```

---

## Case 3: Finished Goods (สินค้าสำเร็จรูป)

**POST**

```json
{
  "orderName": "ORD-FG-001",
  "slaTier": "High",
  "orderItems": [
    {
      "barcode": "FG-BOX-001",
      "pickupLocationCode": "FG-STORE",
      "dropLocationCode": "SHIPPING-DOCK",
      "loadUnitProfileCode": "CARTON-A3",
      "grossWeightKg": 8.5,
      "contents": [
        { "itemNumber": "PRODUCT-X-SN-2024-001", "quantity": 1, "itemStatus": "Good" },
        { "itemNumber": "PRODUCT-X-SN-2024-002", "quantity": 1, "itemStatus": "Good" }
      ]
    }
  ]
}
```

**GET**

```json
{
  "legs": [
    {
      "sequence": 1,
      "pickupLocationCode": "FG-STORE",
      "dropLocationCode": "SHIPPING-DOCK",
      "carrierTypeCode": "SHELF",
      "packages": [
        {
          "barcode": "FG-BOX-001",
          "loadUnitProfileCode": "CARTON-A3",
          "grossWeightKg": 8.5,
          "status": "Pending",
          "contents": [
            { "itemNumber": "PRODUCT-X-SN-2024-001", "quantity": 1, "itemStatus": "Good" },
            { "itemNumber": "PRODUCT-X-SN-2024-002", "quantity": 1, "itemStatus": "Good" }
          ]
        }
      ]
    }
  ]
}
```

---

## Case 4: Packing Material (บรรจุภัณฑ์เปล่า)

กล่องเปล่าบน pallet ส่งไปยังสถานีบรรจุ — ไม่มี `itemStatus`

**POST**

```json
{
  "orderName": "ORD-PKG-001",
  "slaTier": "Low",
  "orderItems": [
    {
      "barcode": "PALLET-PKG-001",
      "pickupLocationCode": "STORE-PKG",
      "dropLocationCode": "PACKING-STATION",
      "loadUnitProfileCode": "PALLET-STD",
      "grossWeightKg": 25.0,
      "contents": [
        { "itemNumber": "CTN-A3-EMPTY", "quantity": 50 },
        { "itemNumber": "CTN-B2-EMPTY", "quantity": 20 }
      ]
    }
  ]
}
```

**GET**

```json
{
  "legs": [
    {
      "sequence": 1,
      "pickupLocationCode": "STORE-PKG",
      "dropLocationCode": "PACKING-STATION",
      "carrierTypeCode": "TUGGER",
      "packages": [
        {
          "barcode": "PALLET-PKG-001",
          "loadUnitProfileCode": "PALLET-STD",
          "grossWeightKg": 25.0,
          "status": "Pending",
          "contents": [
            { "itemNumber": "CTN-A3-EMPTY", "quantity": 50 },
            { "itemNumber": "CTN-B2-EMPTY", "quantity": 20 }
          ]
        }
      ]
    }
  ]
}
```

---

## Case 5: Mixed — ทุก type ใน order เดียว

**POST**

```json
{
  "orderName": "ORD-ALL-TYPES-001",
  "slaTier": "High",
  "orderItems": [
    {
      "barcode": "BOX-MAT-001",
      "pickupLocationCode": "STORE-A",
      "dropLocationCode": "WORKSTATION-01",
      "loadUnitProfileCode": "CARTON-A3",
      "grossWeightKg": 12.0,
      "contents": [
        { "itemNumber": "MOTOR-A", "quantity": 5, "itemStatus": "Good" },
        { "itemNumber": "PCB-B",   "quantity": 3, "itemStatus": "Defective" }
      ]
    },
    {
      "barcode": "TRAY-001",
      "pickupLocationCode": "PREP-B",
      "dropLocationCode": "WORKSTATION-01",
      "loadUnitProfileCode": "TRAY-SMALL",
      "grossWeightKg": 3.5,
      "contents": [
        { "itemNumber": "BOLT-M6", "quantity": 100, "itemStatus": "Good" }
      ]
    },
    {
      "barcode": "JIG-SN-0042",
      "pickupLocationCode": "TOOL-ROOM",
      "dropLocationCode": "LINE-3",
      "loadUnitProfileCode": "FIXTURE-FRAME",
      "grossWeightKg": 15.0
    },
    {
      "barcode": "FG-BOX-001",
      "pickupLocationCode": "FG-STORE",
      "dropLocationCode": "SHIPPING-DOCK",
      "loadUnitProfileCode": "CARTON-A3",
      "grossWeightKg": 8.5,
      "contents": [
        { "itemNumber": "PRODUCT-X-SN-001", "quantity": 1, "itemStatus": "Good" }
      ]
    },
    {
      "barcode": "PALLET-PKG-001",
      "pickupLocationCode": "STORE-PKG",
      "dropLocationCode": "PACKING-STATION",
      "loadUnitProfileCode": "PALLET-STD",
      "grossWeightKg": 25.0,
      "contents": [
        { "itemNumber": "CTN-A3-EMPTY", "quantity": 50 }
      ]
    }
  ]
}
```

**GET** — domain แยก 4 legs อัตโนมัติ

```json
{
  "orderName": "ORD-ALL-TYPES-001",
  "status": "Draft",
  "legs": [
    {
      "sequence": 1,
      "pickupLocationCode": "STORE-A",
      "dropLocationCode": "WORKSTATION-01",
      "carrierTypeCode": "SHELF",
      "packages": [
        {
          "barcode": "BOX-MAT-001",
          "loadUnitProfileCode": "CARTON-A3",
          "grossWeightKg": 12.0,
          "status": "Pending",
          "contents": [
            { "itemNumber": "MOTOR-A", "quantity": 5, "itemStatus": "Good" },
            { "itemNumber": "PCB-B",   "quantity": 3, "itemStatus": "Defective" }
          ]
        }
      ]
    },
    {
      "sequence": 2,
      "pickupLocationCode": "PREP-B",
      "dropLocationCode": "WORKSTATION-01",
      "carrierTypeCode": "FEEDER",
      "packages": [
        {
          "barcode": "TRAY-001",
          "loadUnitProfileCode": "TRAY-SMALL",
          "grossWeightKg": 3.5,
          "status": "Pending",
          "contents": [
            { "itemNumber": "BOLT-M6", "quantity": 100, "itemStatus": "Good" }
          ]
        }
      ]
    },
    {
      "sequence": 3,
      "pickupLocationCode": "TOOL-ROOM",
      "dropLocationCode": "LINE-3",
      "carrierTypeCode": "DIRECT",
      "packages": [
        {
          "barcode": "JIG-SN-0042",
          "loadUnitProfileCode": "FIXTURE-FRAME",
          "grossWeightKg": 15.0,
          "status": "Pending",
          "contents": []
        }
      ]
    },
    {
      "sequence": 4,
      "pickupLocationCode": "FG-STORE",
      "dropLocationCode": "SHIPPING-DOCK",
      "carrierTypeCode": "SHELF",
      "packages": [
        {
          "barcode": "FG-BOX-001",
          "loadUnitProfileCode": "CARTON-A3",
          "grossWeightKg": 8.5,
          "status": "Pending",
          "contents": [
            { "itemNumber": "PRODUCT-X-SN-001", "quantity": 1, "itemStatus": "Good" }
          ]
        }
      ]
    }
  ]
}
```

> หมายเหตุ: `PALLET-PKG-001` (TUGGER) ไม่ปรากฏใน GET เพราะมี pickup/drop ต่างจาก leg อื่น — ถ้า route ตรงกับ leg ที่มีอยู่และ carrierType เดียวกันจะถูก merge เข้า leg เดิมอัตโนมัติ

---

## ItemStatus Values

| Value | ความหมาย | ใช้ตอนไหน |
|-------|---------|----------|
| `Good` | สภาพดี พร้อมใช้ | ก่อนส่ง |
| `Defective` | มีตำหนิ อาจส่งไปซ่อม | ก่อนส่ง |
| `Quarantine` | รอ QC ตรวจสอบ | ก่อนส่ง |
| `Hold` | ระงับ ห้ามเคลื่อนย้าย | ก่อนส่ง |
| `Damaged` | เสียหายระหว่างขนส่ง | หลังส่ง |
| `Rejected` | ปลายทางปฏิเสธรับ | หลังส่ง |

## PackageStatus Values

| Value | ความหมาย |
|-------|---------|
| `Pending` | รอรับ |
| `Picked` | AMR รับแล้ว |
| `Delivered` | ส่งถึงปลายทาง |
| `Failed` | ส่งไม่สำเร็จ |
| `Returned` | ส่งคืน |
| `Cancelled` | ยกเลิก |

---

## CarrierType → AMR Mapping

| CarrierTypeCode | AMR ที่ใช้ | ลักษณะ |
|----------------|-----------|--------|
| `SHELF` | LiftUp AMR | ยก shelf ทั้งก้อน |
| `FEEDER` | Feeder AMR | วางถาดบน AMR โดยตรง |
| `TUGGER` | Tugger AMR | ลาก cart |
| `DIRECT` | General AMR | หยิบโดยตรง |

## Item Type Summary

| Type | contents | itemStatus | ลักษณะพิเศษ |
|------|:--------:|:----------:|------------|
| Material | ✅ | ✅ | track lot/batch |
| Fixture | ❌ | ❌ | serial number, reusable |
| FG | ✅ | ✅ | serial number per unit |
| Packing | ✅ | ❌ | empty containers |
