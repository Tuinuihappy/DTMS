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

export type AuthStatus = "loading" | "authenticated" | "unauthenticated";

type LoginResult = { ok: true } | { ok: false; message: string };

type AuthContextValue = {
  user: AuthUser | null;
  status: AuthStatus;
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
    () => ({ user, status, login, logout }),
    [user, status, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside <AuthProvider>");
  return ctx;
}
