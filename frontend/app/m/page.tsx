import { redirect } from "next/navigation";
import { requireSession } from "@/lib/auth/server-session";

// /m default → /m/trips (or login if no session). Avoids a blank
// landing page when the operator opens the PWA from the home screen.
export default async function OperatorRootPage() {
  await requireSession("/m/login");
  redirect("/m/trips");
}
