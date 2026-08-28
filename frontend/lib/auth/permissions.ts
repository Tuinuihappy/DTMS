// Mirror of the backend permission catalog
// (src/Modules/Iam/DTMS.Iam.Application/Authorization/Permissions.cs, ADR-017).
// Keep in sync with the backend when IAM permissions change. Only the codes the
// frontend actually gates on are mirrored here.
export const Permissions = {
  Iam: {
    SystemRead: "dtms:iam:system:read",
    SubscriptionRead: "dtms:iam:subscription:read",
  },
  DeliveryOrder: {
    ItemRead: "dtms:deliveryorder:item:read",
  },
  Facility: {
    MapRead: "dtms:facility:map:read",
  },
  Fleet: {
    // ADR-019 — renamed from Facility.Profile* when the carrier catalogue
    // moved to Fleet. New codes, so holders need a re-issued grant.
    CarrierTypeRead: "dtms:fleet:carrier-type:read",
    CarrierTypeWrite: "dtms:fleet:carrier-type:write",
  },
  Dispatch: {
    TripRead: "dtms:dispatch:trip:read",
  },
} as const;
