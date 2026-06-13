import "server-only";

import { type NextRequest, NextResponse } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";

// Mirror of orders-export — streams text/csv with Content-Disposition
// passthrough so the browser triggers a file download.
export async function GET(req: NextRequest) {
  const base = process.env.DTMS_BACKEND_URL;
  if (!base) {
    return NextResponse.json(
      { message: "Server misconfigured: DTMS_BACKEND_URL is not set." },
      { status: 500 },
    );
  }

  const token = await getServerToken();
  const qs = req.nextUrl.searchParams.toString();
  const url = `${base.replace(/\/$/, "")}/api/v1/reports/trips-export${qs ? `?${qs}` : ""}`;

  const upstream = await fetch(url, {
    method: "GET",
    headers: {
      Accept: "text/csv",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    cache: "no-store",
  });

  if (!upstream.ok) {
    return NextResponse.json(
      { message: `Export failed: ${upstream.status}` },
      { status: upstream.status },
    );
  }

  return new NextResponse(upstream.body, {
    status: 200,
    headers: {
      "Content-Type": upstream.headers.get("content-type") ?? "text/csv; charset=utf-8",
      "Content-Disposition":
        upstream.headers.get("content-disposition") ?? "attachment; filename=trips.csv",
    },
  });
}
