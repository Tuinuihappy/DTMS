"use client";

import { ThemeProvider } from "next-themes";
import { AuthProvider } from "@/components/auth/auth-provider";
import { ShellProvider } from "@/components/shell/shell-context";
import type { JwtClaims } from "@/lib/auth/jwt";

export function Providers({
  initialClaims,
  children,
}: {
  initialClaims: JwtClaims | null;
  children: React.ReactNode;
}) {
  return (
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      <AuthProvider initialClaims={initialClaims}>
        <ShellProvider>{children}</ShellProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
