import { proxyToBackend } from "@/lib/api/proxy-helpers";

type Ctx = { params: Promise<{ id: string }> };

// Latest manual dispatch attempt for this template. Informational only —
// the dispatch dialog shows it so an operator unsure whether their last
// click landed can check instead of firing a second robot order.
export async function GET(_req: Request, { params }: Ctx) {
  const { id } = await params;
  return proxyToBackend({
    method: "GET",
    path: `/api/v1/order-templates/${id}/last-dispatch`,
  });
}
