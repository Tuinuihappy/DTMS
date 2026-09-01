import { proxyToBackend } from "@/lib/api/proxy-helpers";

export async function GET(
  _req: Request,
  { params }: { params: Promise<{ tripId: string }> },
) {
  const { tripId } = await params;
  return proxyToBackend({
    path: `/api/v1/admin/manual/trips/${encodeURIComponent(tripId)}/pod`,
  });
}
