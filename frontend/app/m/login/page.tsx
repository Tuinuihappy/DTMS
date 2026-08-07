import type { Metadata } from "next";
import { redirectIfSignedIn } from "@/lib/auth/server-session";
import { OperatorLoginForm } from "@/components/operator-pwa/operator-login-form";

export const metadata: Metadata = {
  title: "Sign in — DTMS Operator",
};

export default async function OperatorLoginPage() {
  // Already signed in? Skip the form. /m/trips will revalidate the
  // token anyway when it hits the API.
  await redirectIfSignedIn("/m/trips");
  return <OperatorLoginForm />;
}
