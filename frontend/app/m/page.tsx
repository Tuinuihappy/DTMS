import { redirect } from "next/navigation";
import { cookies } from "next/headers";
import { SESSION_COOKIE } from "@/lib/auth/session";

// /m default → /m/trips (or login if no session). Avoids a blank
// landing page when the operator opens the PWA from the home screen.
export default async function OperatorRootPage() {
  const jar = await cookies();
  if (!jar.get(SESSION_COOKIE)?.value) {
    redirect("/m/login");
  }
  redirect("/m/trips");
}
