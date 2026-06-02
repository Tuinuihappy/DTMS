"use client";

import { ThemeProvider } from "next-themes";
import { ShellProvider } from "@/components/shell/shell-context";

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      <ShellProvider>{children}</ShellProvider>
    </ThemeProvider>
  );
}
