import type { Metadata } from "next";
import { LoginExperience } from "@/components/login/login-experience";

export const metadata: Metadata = {
  title: "Sign in — TMS",
  description: "Sign in to TMS. Move, deliver, arrive — freight that finds its line.",
};

export default function LoginPage() {
  return <LoginExperience />;
}
