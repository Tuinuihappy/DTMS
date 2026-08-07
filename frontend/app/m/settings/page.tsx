import type { Metadata } from "next";
import { requireSession } from "@/lib/auth/server-session";
import { OperatorShellHeader } from "@/components/operator-pwa/operator-shell-header";
import { SettingsPanel } from "@/components/operator-pwa/settings-panel";

export const metadata: Metadata = { title: "Settings — DTMS Operator" };

export default async function OperatorSettingsPage() {
  await requireSession("/m/login");
  return (
    <main className="mx-auto flex min-h-dvh max-w-2xl flex-col">
      <OperatorShellHeader title="Settings" />
      <SettingsPanel />
    </main>
  );
}
