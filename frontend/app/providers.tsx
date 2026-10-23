"use client";

import { MotionConfig } from "motion/react";
import { ThemeProvider } from "next-themes";
import { AuthProvider } from "@/components/auth/auth-provider";
import { ToastProvider } from "@/components/primitives/toast";
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
      {/* reducedMotion="user": the CSS reduced-motion rule in globals.css
          can't reach motion's JS animations — this does, for every
          motion component (transforms and layout; opacity still fades). */}
      <MotionConfig reducedMotion="user">
        <AuthProvider initialClaims={initialClaims}>
          <ShellProvider>
            <ToastProvider>{children}</ToastProvider>
          </ShellProvider>
        </AuthProvider>
      </MotionConfig>
    </ThemeProvider>
  );
}
