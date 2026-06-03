"use client";

import { createContext, useContext } from "react";
import type { ProfileResponse } from "@/app/api/profile/route";
import { useProfile } from "./use-profile";

type ProfileState =
  | { status: "loading"; data: null; error: null }
  | { status: "ready"; data: ProfileResponse; error: null }
  | { status: "error"; data: null; error: string };

const ProfileContext = createContext<ProfileState | null>(null);

export function ProfileProvider({ children }: { children: React.ReactNode }) {
  const state = useProfile();
  return <ProfileContext.Provider value={state}>{children}</ProfileContext.Provider>;
}

export function useProfileData(): ProfileState {
  const ctx = useContext(ProfileContext);
  if (!ctx) throw new Error("useProfileData must be used inside <ProfileProvider>");
  return ctx;
}
