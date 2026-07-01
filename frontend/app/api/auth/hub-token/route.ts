import "server-only";

import { NextResponse } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";

// SignalR can't send Authorization headers on the WebSocket upgrade
// (browser API forbids it) and same-site cookies are blocked on
// cross-origin WebSocket handshakes (frontend runs at :3000 but the
// hub lives at :5219). The standard SignalR workaround is
// `accessTokenFactory` — the client fetches a bearer token from a
// same-origin endpoint and lets SignalR tack it onto the URL as
// `?access_token=…`, which the backend's OnMessageReceived event
// (Program.cs JwtBearer wiring) picks up for `/hubs/*` paths.
//
// This route returns the session JWT verbatim. Cookie is httpOnly,
// so JavaScript can only "read" the token through this same-origin
// fetch — the token never leaves the browser tab's process.
export async function GET() {
  const token = await getServerToken();
  if (!token) {
    return NextResponse.json({ token: null }, { status: 401 });
  }
  return NextResponse.json(
    { token },
    // Realtime clients hit this on every reconnect; make sure a stale
    // 401 or a stale token never gets served from an intermediary cache.
    { headers: { "Cache-Control": "no-store" } },
  );
}
