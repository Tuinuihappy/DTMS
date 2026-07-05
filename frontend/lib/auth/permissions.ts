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
} as const;
