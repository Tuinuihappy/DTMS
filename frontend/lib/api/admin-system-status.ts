// Vendor health snapshot client. The backend maintains an in-memory snapshot
// of every external vendor's health (RIOT3, OMS, ...) via background pollers
// and pushes transitions through DashboardHub.VendorHealthChanged. Calling
// this REST endpoint does NOT trigger a probe — it reads the cached snapshot.

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

// ────────────────────────────────────────────────────────────────────
// Infrastructure readiness — DTMS's view of its own dependencies.
// Powered by ASP.NET Core HealthChecks with tag "ready". Returns 503
// when any check is non-Healthy but the body still itemises each one.

export type InfraCheckStatus = "Healthy" | "Degraded" | "Unhealthy";

export type InfraCheck = {
  name: string;
  status: InfraCheckStatus;
  description: string | null;
  error: string | null;
};

export type InfraReadyResponse = {
  status: InfraCheckStatus;
  checks: InfraCheck[];
};

export async function getInfraHealth(
  signal?: AbortSignal,
): Promise<InfraReadyResponse> {
  const res = await fetch("/api/health/ready", { signal, cache: "no-store" });
  // 200 (Healthy) and 503 (Degraded/Unhealthy) both carry a valid body;
  // anything else is a real transport error worth raising.
  if (res.status !== 200 && res.status !== 503) {
    throw new Error(`Failed to load infrastructure health: ${res.status}`);
  }
  return (await res.json()) as InfraReadyResponse;
}
