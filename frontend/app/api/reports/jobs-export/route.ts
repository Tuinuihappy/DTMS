import "server-only";

import type { NextRequest } from "next/server";
import { proxyCsvExport } from "@/lib/api/csv-export-proxy";

// CSV export — streams the backend file through with its
// Content-Disposition so the browser downloads it (see proxyCsvExport).
export function GET(req: NextRequest) {
  return proxyCsvExport(req, "/api/v1/reports/jobs-export", "jobs.csv");
}
