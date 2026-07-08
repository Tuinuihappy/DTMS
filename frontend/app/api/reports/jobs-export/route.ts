import "server-only";

import { type NextRequest, NextResponse } from "next/server";
import { getServerToken } from "@/lib/auth/server-session";

// CSV export — bypasses the JSON proxy helper so the raw stream + the
// Content-Disposition header pass through and the browser triggers a
// download. Mirrors app/api/reports/orders-export/route.ts.
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
  const url = `${base.replace(/\/$/, "")}/api/v1/reports/jobs-export${qs ? `?${qs}` : ""}`;

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
        upstream.headers.get("content-disposition") ?? "attachment; filename=jobs.csv",
    },
  });
}
