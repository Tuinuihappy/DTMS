"use client";

import { createContext, useCallback, useContext, useState } from "react";

type ShellContextValue = {
  railDrawerOpen: boolean;
  openRailDrawer: () => void;
  closeRailDrawer: () => void;
  toggleRailDrawer: () => void;
};

const ShellContext = createContext<ShellContextValue | null>(null);

export function ShellProvider({ children }: { children: React.ReactNode }) {
  const [railDrawerOpen, setRailDrawerOpen] = useState(false);

  const openRailDrawer = useCallback(() => setRailDrawerOpen(true), []);
  const closeRailDrawer = useCallback(() => setRailDrawerOpen(false), []);
  const toggleRailDrawer = useCallback(() => setRailDrawerOpen((v) => !v), []);

  return (
    <ShellContext.Provider
      value={{ railDrawerOpen, openRailDrawer, closeRailDrawer, toggleRailDrawer }}
    >
      {children}
    </ShellContext.Provider>
  );
}

export function useShell() {
  const ctx = useContext(ShellContext);
  if (!ctx) throw new Error("useShell must be used inside <ShellProvider>");
  return ctx;
}
