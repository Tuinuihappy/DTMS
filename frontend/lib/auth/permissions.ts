// Mirror of the backend permission catalog
// (src/Modules/Iam/DTMS.Iam.Application/Authorization/Permissions.cs, ADR-017).
// Keep in sync with the backend when IAM permissions change. Only the codes the
// frontend actually gates on are mirrored here.
export const Permissions = {
  Iam: {
    SystemRead: "dtms:iam:system:read",
    RoleRead: "dtms:iam:role:read",
    SubscriptionRead: "dtms:iam:subscription:read",
  },
  DeliveryOrder: {
    ItemRead: "dtms:deliveryorder:item:read",
  },
  Facility: {
    ProfileRead: "dtms:facility:profile:read",
    ProfileWrite: "dtms:facility:profile:write",
    MapRead: "dtms:facility:map:read",
  },
  Dispatch: {
    TripRead: "dtms:dispatch:trip:read",
  },
} as const;
