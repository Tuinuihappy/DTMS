// WMS PR-4 — read the deployment-level feature flags so the frontend
// can hide Manual/Fleet options when Wms.Enabled is false. Cached once
// per page load; the flags don't flip during a session.

import { useEffect, useState } from "react";

export type SystemCapabilitiesDto = {
  wmsEnabled: boolean;
  enabledTransportModes: string[];   // "Amr" | "Manual" | "Fleet"
};

const DEFAULT_CAPABILITIES: SystemCapabilitiesDto = {
  wmsEnabled: false,
  enabledTransportModes: ["Amr"],
};

export async function getSystemCapabilities(): Promise<SystemCapabilitiesDto> {
  try {
    const res = await fetch("/api/system/capabilities", { cache: "no-store" });
    if (!res.ok) return DEFAULT_CAPABILITIES;
    return res.json();
  } catch {
    // Network / backend down — fall back to AMR-only so the UI still
    // renders instead of blanking out.
    return DEFAULT_CAPABILITIES;
  }
}

export function useSystemCapabilities(): SystemCapabilitiesDto {
  const [caps, setCaps] = useState<SystemCapabilitiesDto>(DEFAULT_CAPABILITIES);

  useEffect(() => {
    let cancelled = false;
    getSystemCapabilities().then((c) => {
      if (!cancelled) setCaps(c);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  return caps;
}
