import type { Metadata } from "next";
import { ManualOperatorBoard } from "@/components/dispatch/manual/manual-operator-board";

export const metadata: Metadata = {
  title: "Manual operators — Dispatch",
  description: "Manual transport mode operator board, override approvals, and trip reassignment.",
};

export default function DispatchManualPage() {
  return <ManualOperatorBoard />;
}
