import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { SESSION_COOKIE } from "@/lib/auth/session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { SettingsPanel } from "@/components/operator-pwa/settings-panel";

export const metadata: Metadata = { title: "Settings — DTMS Operator" };

export default async function OperatorSettingsPage() {
  const jar = await cookies();
  if (!jar.get(SESSION_COOKIE)?.value) {
    redirect("/m/login");
  }
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <OperatorShellHeader title="Settings" />
      <SettingsPanel />
    </main>
  );
}
