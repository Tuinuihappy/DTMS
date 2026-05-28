// Centralized TanStack Query cache keys so invalidation stays consistent.
// Use the factory pattern — every consumer references the same tuple,
// preventing typos that silently break cache invalidation.

export const queryKeys = {
  actionTemplates: {
    all: ["action-templates"] as const,
    list: (filters?: { includeInactive?: boolean; actionType?: string }) =>
      [...queryKeys.actionTemplates.all, "list", filters ?? {}] as const,
    detail: (id: string) =>
      [...queryKeys.actionTemplates.all, "detail", id] as const,
  },
  orderTemplates: {
    all: ["order-templates"] as const,
    list: (filters?: { includeInactive?: boolean }) =>
      [...queryKeys.orderTemplates.all, "list", filters ?? {}] as const,
    detail: (id: string) =>
      [...queryKeys.orderTemplates.all, "detail", id] as const,
  },
} as const;
