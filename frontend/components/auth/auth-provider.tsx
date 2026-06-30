"use client";

import { useRouter } from "next/navigation";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import type { JwtClaims } from "@/lib/auth/jwt";
import { PROFILE_STORAGE_KEY, type AuthUser } from "@/lib/auth/session";
import { getMyPermissions } from "@/lib/api/iam-systems";
import { matches } from "@/lib/api/iam";

export type AuthStatus = "loading" | "authenticated" | "unauthenticated";

type LoginResult = { ok: true } | { ok: false; message: string };

type AuthContextValue = {
  user: AuthUser | null;
  status: AuthStatus;
  // Phase S.6 — UI-side permission gating. `permissions` is the raw set
  // of codes the backend stamped on this principal (including wildcards
  // like `dtms:*`). `hasPermission` matches with the same logic the
  // backend's PermissionAuthorizationHandler uses, so a wildcard grant
  // covers everything under it. `null` while still loading — UI should
  // wait or show a loading state rather than incorrectly assume "no
  // permissions".
  permissions: string[] | null;
  hasPermission: (code: string) => boolean;
  login: (username: string, password: string) => Promise<LoginResult>;
  logout: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

function hydrateFromStorage(): AuthUser | null {
  if (typeof window === "undefined") return null;
  try {
    const raw = window.localStorage.getItem(PROFILE_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<AuthUser>;
    if (!parsed.employeeCode || !parsed.displayName || !parsed.role) return null;
    return {
      employeeCode: parsed.employeeCode,
      displayName: parsed.displayName,
      role: parsed.role,
      thumbnailPhoto: parsed.thumbnailPhoto ?? "",
    };
  } catch {
    return null;
  }
}

function userFromClaims(claims: JwtClaims): AuthUser {
  return {
    employeeCode: claims.employeeCode,
    displayName: claims.username,
    role: claims.role,
    thumbnailPhoto: "",
  };
}

export function AuthProvider({
  initialClaims,
  children,
}: {
  initialClaims: JwtClaims | null;
  children: React.ReactNode;
}) {
  const router = useRouter();
  const [user, setUser] = useState<AuthUser | null>(() =>
    initialClaims ? userFromClaims(initialClaims) : null,
  );
  const [status, setStatus] = useState<AuthStatus>(
    initialClaims ? "authenticated" : "unauthenticated",
  );
  // null = loading / no fetch attempted yet; [] = fetched, no permissions
  // (legitimately empty set). UI gates should treat null as "wait" and []
  // as "deny everything except wildcard grants".
  const [permissions, setPermissions] = useState<string[] | null>(null);

  // After mount, merge in the richer profile (displayName, photo) cached
  // in localStorage on the previous login. Server-rendered initialClaims
  // gives us a flash-free authenticated state; localStorage upgrades it
  // with the display fields the JWT can't carry.
  useEffect(() => {
    if (!initialClaims) return;
    const cached = hydrateFromStorage();
    if (cached && cached.employeeCode === initialClaims.employeeCode) {
      setUser(cached);
    }
  }, [initialClaims]);

  // Phase S.6 — fetch the principal's effective permission set whenever
  // identity becomes authenticated. Re-fetch on identity change (logout
  // then login as a different user clears the previous set first).
  useEffect(() => {
    if (status !== "authenticated") {
      setPermissions(null);
      return;
    }
    const ctrl = new AbortController();
    getMyPermissions(ctrl.signal)
      .then((perms) => setPermissions(perms))
      .catch(() => setPermissions([]));   // network/auth failure: deny by default
    return () => ctrl.abort();
  }, [status, user?.employeeCode]);

  const hasPermission = useCallback(
    (code: string) => {
      if (permissions === null) return false;   // still loading — gate hides
      return permissions.some((held) => matches(held, code));
    },
    [permissions],
  );

  const login = useCallback<AuthContextValue["login"]>(
    async (username, password) => {
      try {
        const res = await fetch("/api/auth/login", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ username, password }),
        });
        if (!res.ok) {
          let message = "Sign-in failed. Please try again.";
          try {
            const body = await res.json();
            if (typeof body?.message === "string") message = body.message;
          } catch {
            /* ignore */
          }
          return { ok: false, message };
        }
        const { user: nextUser } = (await res.json()) as { user: AuthUser };
        try {
          window.localStorage.setItem(PROFILE_STORAGE_KEY, JSON.stringify(nextUser));
        } catch {
          /* storage full / disabled — non-fatal */
        }
        setUser(nextUser);
        setStatus("authenticated");
        return { ok: true };
      } catch {
        return {
          ok: false,
          message: "Network error. Check your connection and try again.",
        };
      }
    },
    [],
  );

  const logout = useCallback(async () => {
    try {
      await fetch("/api/auth/logout", { method: "POST" });
    } catch {
      /* ignore — we still clear local state */
    }
    try {
      window.localStorage.removeItem(PROFILE_STORAGE_KEY);
    } catch {
      /* ignore */
    }
    setUser(null);
    setStatus("unauthenticated");
    router.push("/login");
  }, [router]);

  const value = useMemo<AuthContextValue>(
    () => ({ user, status, permissions, hasPermission, login, logout }),
    [user, status, permissions, hasPermission, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}
