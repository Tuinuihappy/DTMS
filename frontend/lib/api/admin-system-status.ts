// Vendor + infrastructure health snapshot client. The backend maintains
// an in-memory snapshot of every component (external vendors AND DTMS's
// own infra dependencies) via background pollers and pushes transitions
// through DashboardHub.VendorHealthChanged. Calling this REST endpoint
// does NOT trigger a probe — it reads the cached snapshot.
//
// Vendor names use an "infra:" prefix for internal dependencies
// (postgres / redis / rabbitmq / masstransit-bus) so the frontend can
// split them into separate sections without a schema field.

export type VendorHealthStatus = "Unknown" | "Healthy" | "Degraded" | "Unhealthy";

export type VendorHealthSnapshot = {
  vendor: string;
  status: VendorHealthStatus;
  code: string | null;
  message: string | null;
  failureReason: string | null;
  latencyMs: number | null;
  lastChangedAt: string;
  lastCheckedAt: string;
  consecutiveSuccesses: number;
  consecutiveFailures: number;
};

export type VendorHealthResponse = {
  vendors: VendorHealthSnapshot[];
};

export async function getVendorHealth(
  signal?: AbortSignal,
): Promise<VendorHealthResponse> {
  const res = await fetch("/api/vendors/health", { signal, cache: "no-store" });
  if (!res.ok) throw new Error(`Failed to load vendor health: ${res.status}`);
  return (await res.json()) as VendorHealthResponse;
}

export const INFRA_PREFIX = "infra:";

export function isInfra(snapshot: VendorHealthSnapshot): boolean {
  return snapshot.vendor.startsWith(INFRA_PREFIX);
}

export function displayName(snapshot: VendorHealthSnapshot): string {
  return snapshot.vendor.startsWith(INFRA_PREFIX)
    ? snapshot.vendor.slice(INFRA_PREFIX.length)
    : snapshot.vendor;
}
