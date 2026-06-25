import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/auth/session";
import { OperatorLoginForm } from "@/components/operator-pwa/operator-login-form";

export const metadata: Metadata = {
  title: "Sign in — DTMS Operator",
};

export default async function OperatorLoginPage() {
  // Already signed in? Skip the form. /m/trips will revalidate the
  // token anyway when it hits the API.
  const jar = await cookies();
  if (jar.get(SESSION_COOKIE)?.value) {
    redirect("/m/trips");
  }
  return <OperatorLoginForm />;
}
