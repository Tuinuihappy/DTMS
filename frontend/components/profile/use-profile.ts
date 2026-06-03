"use client";

import { useEffect, useState } from "react";
import type { ProfileResponse } from "@/app/api/profile/route";

type State =
  | { status: "loading"; data: null; error: null }
  | { status: "ready"; data: ProfileResponse; error: null }
  | { status: "error"; data: null; error: string };

/**
 * Fetch the live profile from /api/profile (which proxies the upstream
 * /users/{employeeCode} endpoint with the session Bearer token attached
 * server-side). One-shot per mount — the profile doesn't change often
 * enough to warrant polling, and the page re-mounts on navigation.
 */
export function useProfile() {
  const [state, setState] = useState<State>({
    status: "loading",
    data: null,
    error: null,
  });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch("/api/profile", { cache: "no-store" });
        if (!res.ok) {
          const body = (await res.json().catch(() => ({}))) as { message?: string };
          if (!cancelled)
            setState({
              status: "error",
              data: null,
              error: body.message ?? `Profile request failed (${res.status}).`,
            });
          return;
        }
        const data = (await res.json()) as ProfileResponse;
        if (!cancelled) setState({ status: "ready", data, error: null });
      } catch (err) {
        if (!cancelled)
          setState({
            status: "error",
            data: null,
            error: (err as Error).message ?? "Network error",
          });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
